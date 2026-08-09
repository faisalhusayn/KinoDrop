import Foundation
import ActivityKit
import Photos
import PhotosUI
import SwiftUI
import UIKit
import UserNotifications

private struct PersistedUpload: Codable {
    let name: String
    let remotePath: String
    let partialRemotePath: String?
    let bookmarkData: Data
}

private struct PersistedDownload: Codable {
    let name: String
    let remotePath: String
    let totalBytes: Int64?
    let partialPath: String
}

private enum TransferValidationError: LocalizedError {
    case sizeMismatch(expected: Int64, actual: Int64)

    var errorDescription: String? {
        switch self {
        case let .sizeMismatch(expected, actual):
            return "Transfer verification failed: expected \(expected) bytes, received \(actual) bytes."
        }
    }
}

enum TransferConflictChoice {
    case overwrite
    case rename
    case skip
}

struct TransferConflict {
    let name: String
    let remotePath: String
}

@MainActor
final class AppModel: ObservableObject {
    enum ConnectionState: Equatable {
        case disconnected
        case connecting
        case connected
        case failed(String)

        var label: String {
            switch self {
            case .disconnected: return "Not connected"
            case .connecting: return "Connecting..."
            case .connected: return "Connected"
            case .failed(let message): return message
            }
        }
    }

    @Published var config: ConnectionConfig
    @Published var connectionState: ConnectionState = .disconnected
    @Published var smbDiagnostics: SMBConnectionDiagnostics?
    @Published var transfers: [TransferItem] = []
    @Published var remoteFiles: [RemoteFile] = []
    @Published var browsePath = ""
    @Published var errorMessage: String?
    @Published var showQRScanner = false
    @Published var shareURL: URL?
    @Published var liveActivityStatus: String?
    @Published var partialFileCount = 0
    @Published var partialStorageBytes: Int64 = 0
    @Published var transferHistory: [TransferHistoryItem] = []
    @Published var conflictRequest: TransferConflict?
    @Published var nearbyDevices: [NearbyDevice] = []
    @Published var savedConnections: [SavedConnection] = []
    @Published private(set) var isQueuePaused = false

    let smb = SMBClient()
    private let keychain = KeychainStore()
    private let nearbyBrowser = NearbyDeviceBrowser()
    private var cleanupURLs: [UUID: URL] = [:]
    private var scopedURLs: [UUID: URL] = [:]
    private enum TransferKind {
        case upload(localURL: URL, remotePath: String, partialRemotePath: String)
        case download(remotePath: String, partialURL: URL, finalURL: URL)
    }
    private struct TransferJob {
        let id: UUID
        let kind: TransferKind
    }
    private var jobs: [UUID: TransferJob] = [:]
    private var pendingJobs: [UUID] = []
    private var activeTask: Task<Void, Never>?
    private var activeTransferID: UUID?
    private var pauseRequested = false
    private var isInBackground = false
    private var backgroundTaskID: UIBackgroundTaskIdentifier = .invalid
    private let persistedQueueKey = "pendingUploadQueue"
    private let persistedDownloadQueueKey = "pendingDownloadQueue"
    private let historyKey = "transferHistory"
    private var liveActivity: Activity<TransferActivityAttributes>?
    private var conflictContinuation: CheckedContinuation<TransferConflictChoice, Never>?

    init() {
        savedConnections = keychain.loadConnections()
        config = savedConnections.first?.config ?? .default
        restorePersistedUploads()
        restorePersistedDownloads()
        loadHistory()
        refreshPartialFileSummary()
        requestNotificationPermission()
        nearbyBrowser.onChange = { [weak self] devices in self?.nearbyDevices = devices }
        nearbyBrowser.start()
        Task { @MainActor [weak self] in
            await self?.autoReconnectSavedConnection()
        }
    }

    var isConnected: Bool { connectionState == .connected }

    private func autoReconnectSavedConnection() async {
        guard !config.host.isEmpty, !config.password.isEmpty else { return }
        try? await Task.sleep(nanoseconds: 300_000_000)
        guard connectionState == .disconnected else { return }
        await connect()
    }

    func connect() async {
        errorMessage = nil
        connectionState = .connecting

        do {
            try await smb.connect(using: config)
            savedConnections = try keychain.saveConnection(config)
            smbDiagnostics = smb.diagnostics
            connectionState = .connected
            await refreshFiles()
            startNextTransfer()
        } catch {
            connectionState = .failed(error.localizedDescription)
            errorMessage = error.localizedDescription
        }
    }

    func disconnect() async {
        pauseRequested = false
        if let activeTransferID {
            updateTransfer(activeTransferID) { $0.state = .cancelled }
            cleanupDownloadResource(for: activeTransferID)
            cleanupUploadResources(for: activeTransferID)
        }
        activeTask?.cancel()
        activeTask = nil
        activeTransferID = nil
        for id in pendingJobs {
            updateTransfer(id) { $0.state = .cancelled }
            cleanupUploadResources(for: id)
            cleanupDownloadResource(for: id)
        }
        pendingJobs.removeAll()
        await smb.disconnect()
        smbDiagnostics = nil
        connectionState = .disconnected
        remoteFiles = []
        persistQueue()
    }

    func handleScenePhase(_ phase: ScenePhase) {
        switch phase {
        case .active:
            isInBackground = false
            pauseRequested = isQueuePaused
            if backgroundTaskID != .invalid {
                UIApplication.shared.endBackgroundTask(backgroundTaskID)
                backgroundTaskID = .invalid
            }
            startNextTransfer()
        case .background:
            isInBackground = true
            // Let the active transfer use iOS's temporary background execution window.
            backgroundTaskID = UIApplication.shared.beginBackgroundTask(withName: "KinoDrop transfer") { [weak self] in
                Task { @MainActor in
                    self?.backgroundTaskExpired()
                }
            }
        case .inactive:
            break
        @unknown default:
            break
        }
    }

    private func backgroundTaskExpired() {
        pauseRequested = true
        activeTask?.cancel()
        if backgroundTaskID != .invalid {
            UIApplication.shared.endBackgroundTask(backgroundTaskID)
            backgroundTaskID = .invalid
        }
    }

    func applyScannedURL(_ url: URL) {
        guard url.scheme?.lowercased() == "smb",
              let host = url.host else {
            errorMessage = "That QR code is not a KinoDrop SMB connection."
            return
        }

        config.host = host
        let share = url.path.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        if !share.isEmpty { config.share = share }

        if let queryItems = URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems {
            if let username = queryItems.first(where: { $0.name == "user" })?.value,
               !username.isEmpty {
                config.username = username
            }
            if let password = queryItems.first(where: { $0.name == "password" })?.value,
               !password.isEmpty {
                config.password = password
            }
        }
    }

    func useNearbyDevice(_ device: NearbyDevice) {
        config.host = device.host
        config.share = device.share
    }

    func useSavedConnection(_ connection: SavedConnection) {
        config = connection.config
    }

    func deleteSavedConnection(_ connection: SavedConnection) {
        guard (try? keychain.deleteConnection(connection.id)) != nil else { return }
        savedConnections.removeAll { $0.id == connection.id }
        if config == connection.config { config = savedConnections.first?.config ?? .default }
    }

    func refreshNearbyDevices() {
        nearbyBrowser.start()
    }

    func refreshFiles() async {
        guard isConnected else { return }

        do {
            remoteFiles = try await smb.listDirectory(path: browsePath)
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func open(_ file: RemoteFile) async {
        guard file.isDirectory else { return }
        browsePath = file.path
        await refreshFiles()
    }

    func goUp() async {
        guard !browsePath.isEmpty else { return }
        browsePath = browsePath.split(separator: "/").dropLast().joined(separator: "/")
        await refreshFiles()
    }

    func enqueueUploads(urls: [URL]) {
        for url in urls {
            enqueueUpload(localURL: url, remoteName: url.lastPathComponent)
        }
    }

    private func enqueueUpload(
        localURL: URL,
        remoteName: String,
        preparationDuration: TimeInterval? = nil,
        cleanup: Bool = false,
        alreadyScopedURL: URL? = nil) {
        let remotePath = browsePath.isEmpty ? remoteName : "\(browsePath)/\(remoteName)"
        let transfer = TransferItem(
            name: remoteName,
            direction: .upload,
            totalBytes: fileSize(localURL),
            preparationDuration: preparationDuration)
        transfers.append(transfer)
        let id = transfer.id
        if cleanup { cleanupURLs[id] = localURL }
        if let alreadyScopedURL { scopedURLs[id] = alreadyScopedURL }
        let partialRemotePath = "\(remotePath).kinodrop-\(id.uuidString).part"
        jobs[id] = TransferJob(
            id: id,
            kind: .upload(
                localURL: localURL,
                remotePath: remotePath,
                partialRemotePath: partialRemotePath))
        pendingJobs.append(id)
        persistQueue()
        startNextTransfer()
    }

    func importPhotos(_ items: [PhotosPickerItem]) async {
        let preparationStart = Date()
        for item in items {
            guard let picked = try? await item.loadTransferable(type: PickedFile.self) else { continue }
            let preparationDuration = Date().timeIntervalSince(preparationStart)
            let sourceURL = persistentPhotoCopy(for: picked.url) ?? picked.url
            enqueueUpload(
                localURL: sourceURL,
                remoteName: picked.filename,
                preparationDuration: preparationDuration,
                cleanup: true)
            if sourceURL != picked.url { try? FileManager.default.removeItem(at: picked.url) }
        }
    }

    func importFiles(_ urls: [URL]) async {
        for url in urls {
            let accessed = url.startAccessingSecurityScopedResource()
            enqueueUpload(
                localURL: url,
                remoteName: url.lastPathComponent,
                alreadyScopedURL: accessed ? url : nil)
        }
    }

    private func startNextTransfer() {
        guard activeTask == nil, !pauseRequested, !isInBackground, isConnected, let id = pendingJobs.first,
              let job = jobs[id] else { return }
        pendingJobs.removeFirst()
        activeTransferID = id
        activeTask = Task { [weak self] in
            await self?.perform(job)
        }
    }

    private func perform(_ job: TransferJob) async {
        var jobToRun = job
        if case let .upload(localURL, remotePath, _) = job.kind,
           (try? await smb.remoteFileSize(at: remotePath)) != nil {
            switch await waitForConflict(name: transfers.first(where: { $0.id == job.id })?.name ?? remotePath, remotePath: remotePath) {
            case .overwrite:
                break
            case .skip:
                updateTransfer(job.id) { $0.state = .cancelled }
                activeTransferID = nil
                activeTask = nil
                cleanupUploadResources(for: job.id)
                persistQueue()
                startNextTransfer()
                return
            case .rename:
                let renamedPath = renamedRemotePath(remotePath)
                jobToRun = TransferJob(
                    id: job.id,
                    kind: .upload(
                        localURL: localURL,
                        remotePath: renamedPath,
                        partialRemotePath: "\(renamedPath).kinodrop-\(job.id.uuidString).part"))
            }
        }
        updateTransfer(job.id) {
            $0.state = .transferring
            $0.startedAt = Date()
        }
        if let transfer = transfers.first(where: { $0.id == job.id }) {
            await beginLiveActivity(for: transfer)
        }
        let progressThrottle = ProgressThrottle()
        let transferStart = Date()

        do {
            var attempt = 0
            while true {
                do {
                    try await execute(jobToRun, progressThrottle: progressThrottle)
                    if Task.isCancelled { throw CancellationError() }
                    break
                } catch {
                    if Task.isCancelled { throw CancellationError() }
                    guard attempt < 2 else { throw error }
                    attempt += 1
                    try await Task.sleep(nanoseconds: UInt64(attempt) * 500_000_000)
                }
            }
            updateTransfer(job.id) {
                $0.transferDuration = Date().timeIntervalSince(transferStart)
                if let totalBytes = $0.totalBytes { $0.completedBytes = totalBytes }
                $0.state = .completed
            }
            if case let .download(_, partialURL, finalURL) = jobToRun.kind {
                try? FileManager.default.removeItem(at: finalURL)
                try? FileManager.default.moveItem(at: partialURL, to: finalURL)
                shareURL = finalURL
            }
            cleanupUploadResources(for: job.id)
            recordHistory(for: job.id, result: "Completed")
            sendTransferNotification(title: "Transfer complete", body: transfers.first(where: { $0.id == job.id })?.name ?? "File transfer finished")
            await endLiveActivity(phase: "Completed", transferID: job.id)
        } catch {
            let wasPaused = pauseRequested
            updateTransfer(job.id) {
                $0.transferDuration = Date().timeIntervalSince(transferStart)
                $0.state = wasPaused ? .paused : (Task.isCancelled ? .cancelled : .failed(error.localizedDescription))
            }
            if wasPaused {
                pendingJobs.insert(job.id, at: 0)
            } else if Task.isCancelled {
                cleanupUploadResources(for: job.id)
                cleanupDownloadResource(for: job.id)
            } else {
                cleanupDownloadResource(for: job.id)
            }
            if !wasPaused {
                recordHistory(for: job.id, result: "Failed")
                sendTransferNotification(title: "Transfer failed", body: error.localizedDescription)
            }
            await endLiveActivity(phase: wasPaused ? "Paused" : "Stopped", transferID: job.id)
        }

        activeTransferID = nil
        activeTask = nil
        persistQueue()
        startNextTransfer()
    }

    private func execute(_ job: TransferJob, progressThrottle: ProgressThrottle) async throws {
        switch job.kind {
        case let .upload(localURL, remotePath, partialRemotePath):
            let localSize = fileSize(localURL) ?? 0
            let existingSize = try await smb.remoteFileSize(at: partialRemotePath)
            let offset: Int64
            if let existingSize, existingSize >= 0, existingSize <= localSize {
                offset = existingSize
            } else {
                if existingSize != nil { try? await smb.remove(remotePath: partialRemotePath) }
                offset = 0
            }

            if let existingSize, existingSize == localSize {
                updateTransferProgress(
                    job.id,
                    progress: SMBProgress(completed: localSize, total: localSize))
            } else {
                try await smb.writeTransferMetadata(
                    remotePath: "\(remotePath).kinodrop-meta",
                    partialPath: partialRemotePath,
                    totalBytes: localSize)
                try await smb.upload(localURL: localURL, remotePath: partialRemotePath, offset: offset, total: localSize) { [weak self] progress in
                    guard !Task.isCancelled else { return false }
                    if progressThrottle.shouldEmit() {
                        Task { @MainActor [weak self] in
                            self?.updateTransferProgress(job.id, progress: progress)
                        }
                    }
                    return true
                }
            }
            try? await smb.remove(remotePath: remotePath)
            try await smb.move(remotePath: partialRemotePath, to: remotePath)
            try? await smb.remove(remotePath: "\(remotePath).kinodrop-meta")
            let finalSize = try await smb.remoteFileSize(at: remotePath) ?? -1
            guard finalSize == localSize else {
                throw TransferValidationError.sizeMismatch(expected: localSize, actual: finalSize)
            }
        case let .download(remotePath, partialURL, _):
            let partialSize = fileSize(partialURL) ?? 0
            let offset: Int64
            if let expectedSize = transfers.first(where: { $0.id == job.id })?.totalBytes,
               partialSize > expectedSize {
                try? FileManager.default.removeItem(at: partialURL)
                offset = 0
            } else {
                offset = partialSize
            }
            try await smb.download(remotePath: remotePath, localURL: partialURL, offset: offset) { [weak self] progress in
                guard !Task.isCancelled else { return false }
                if progressThrottle.shouldEmit() {
                    Task { @MainActor [weak self] in
                        self?.updateTransferProgress(job.id, progress: progress)
                    }
                }
                return true
            }
            if let expectedSize = transfers.first(where: { $0.id == job.id })?.totalBytes {
                let actualSize = fileSize(partialURL) ?? 0
                guard actualSize == expectedSize else {
                    throw TransferValidationError.sizeMismatch(expected: expectedSize, actual: actualSize)
                }
            }
        }
    }

    private func waitForConflict(name: String, remotePath: String) async -> TransferConflictChoice {
        conflictRequest = TransferConflict(name: name, remotePath: remotePath)
        return await withCheckedContinuation { continuation in
            conflictContinuation = continuation
        }
    }

    func resolveConflict(_ choice: TransferConflictChoice) {
        conflictRequest = nil
        conflictContinuation?.resume(returning: choice)
        conflictContinuation = nil
    }

    private func renamedRemotePath(_ path: String) -> String {
        let components = path.split(separator: "/")
        let name = String(components.last ?? "file")
        let parent = components.dropLast().joined(separator: "/")
        let renamed = "\(name) (copy)-\(Int(Date().timeIntervalSince1970))"
        return parent.isEmpty ? renamed : "\(parent)/\(renamed)"
    }

    func download(_ file: RemoteFile) {
        let transfer = TransferItem(
            name: file.name,
            direction: .download,
            totalBytes: file.size)
        transfers.append(transfer)
        let id = transfer.id
        let downloadsDirectory = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Downloads", isDirectory: true)
        try? FileManager.default.createDirectory(at: downloadsDirectory, withIntermediateDirectories: true)
        let partialURL = downloadsDirectory.appendingPathComponent("\(id.uuidString).part")
        let finalURL = FileManager.default.temporaryDirectory.appendingPathComponent(file.name)
        try? FileManager.default.removeItem(at: finalURL)
        jobs[id] = TransferJob(
            id: id,
            kind: .download(remotePath: file.path, partialURL: partialURL, finalURL: finalURL))
        pendingJobs.append(id)
        persistQueue()
        startNextTransfer()
    }

    func cancel(_ transfer: TransferItem) {
        guard let index = transfers.firstIndex(where: { $0.id == transfer.id }) else { return }
        if activeTransferID == transfer.id {
            updateTransfer(transfer.id) { $0.state = .cancelled }
            activeTask?.cancel()
            cleanupUploadResources(for: transfer.id)
            cleanupDownloadResource(for: transfer.id)
            persistQueue()
        } else if let pendingIndex = pendingJobs.firstIndex(of: transfer.id) {
            pendingJobs.remove(at: pendingIndex)
            updateTransfer(transfer.id) { $0.state = .cancelled }
            cleanupUploadResources(for: transfer.id)
            cleanupDownloadResource(for: transfer.id)
            persistQueue()
        } else if case .paused = transfers[index].state {
            updateTransfer(transfer.id) { $0.state = .cancelled }
            cleanupUploadResources(for: transfer.id)
            cleanupDownloadResource(for: transfer.id)
            persistQueue()
        }
    }

    func toggleQueuePause() {
        isQueuePaused.toggle()
        pauseRequested = isQueuePaused
        if isQueuePaused {
            activeTask?.cancel()
        } else {
            startNextTransfer()
        }
        persistQueue()
    }

    func retryAllFailed() {
        for transfer in transfers where transfer.isRetryable {
            guard !pendingJobs.contains(transfer.id) else { continue }
            updateTransfer(transfer.id) {
                $0.state = .queued
                $0.completedBytes = 0
                $0.transferDuration = nil
            }
            pendingJobs.append(transfer.id)
        }
        persistQueue()
        startNextTransfer()
    }

    func cancelAllQueued() {
        activeTask?.cancel()
        if let activeTransferID {
            updateTransfer(activeTransferID) { $0.state = .cancelled }
            cleanupUploadResources(for: activeTransferID)
            cleanupDownloadResource(for: activeTransferID)
        }
        for id in pendingJobs {
            updateTransfer(id) { $0.state = .cancelled }
            cleanupUploadResources(for: id)
            cleanupDownloadResource(for: id)
        }
        pendingJobs.removeAll()
        activeTransferID = nil
        activeTask = nil
        persistQueue()
    }

    func retry(_ transfer: TransferItem) {
        guard transfer.isRetryable, jobs[transfer.id] != nil else { return }
        updateTransfer(transfer.id) {
            $0.state = .queued
            $0.completedBytes = 0
            $0.transferDuration = nil
        }
        pendingJobs.append(transfer.id)
        persistQueue()
        startNextTransfer()
    }

    func clearFinishedTransfers() {
        let removableIDs = transfers.compactMap { transfer -> UUID? in
            switch transfer.state {
            case .completed, .cancelled: return transfer.id
            default: return nil
            }
        }
        transfers.removeAll { removableIDs.contains($0.id) }
        for id in removableIDs {
            jobs.removeValue(forKey: id)
            cleanupUploadResources(for: id)
        }
        persistQueue()
    }

    private func updateTransfer(_ id: UUID, update: (inout TransferItem) -> Void) {
        guard let index = transfers.firstIndex(where: { $0.id == id }) else { return }
        update(&transfers[index])
    }

    private func updateTransferProgress(_ id: UUID, progress: SMBProgress) {
        updateTransfer(id) {
            let now = Date()
            if let startedAt = $0.startedAt, progress.completed > 0 {
                let elapsed = max(now.timeIntervalSince(startedAt), 0.001)
                $0.bytesPerSecond = Double(progress.completed) / elapsed
            }
            $0.completedBytes = progress.completed
            if let total = progress.total { $0.totalBytes = total }
        }
        Task { @MainActor [weak self] in
            await self?.updateLiveActivity(transferID: id)
        }
    }

    private func beginLiveActivity(for transfer: TransferItem) async {
        guard ActivityAuthorizationInfo().areActivitiesEnabled else {
            liveActivityStatus = "Live Activities are disabled in iOS Settings."
            return
        }
        let attributes = TransferActivityAttributes(
            fileName: transfer.name,
            direction: transfer.direction == .upload ? "upload" : "download")
        let state = TransferActivityAttributes.ContentState(
            phase: transfer.direction == .upload ? "Uploading" : "Downloading",
            completedBytes: transfer.completedBytes,
            totalBytes: transfer.totalBytes)
        do {
            liveActivity = try Activity.request(
                attributes: attributes,
                content: ActivityContent(state: state, staleDate: nil),
                pushType: nil)
            liveActivityStatus = "Live Activity started."
        } catch {
            liveActivityStatus = "Live Activity unavailable: \(error.localizedDescription)"
        }
    }

    private func updateLiveActivity(transferID: UUID) async {
        guard let activity = liveActivity,
              let transfer = transfers.first(where: { $0.id == transferID }) else { return }
        let phase = transfer.direction == .upload ? "Uploading" : "Downloading"
        let state = TransferActivityAttributes.ContentState(
            phase: phase,
            completedBytes: transfer.completedBytes,
            totalBytes: transfer.totalBytes)
        await activity.update(ActivityContent(state: state, staleDate: nil))
    }

    private func endLiveActivity(phase: String, transferID: UUID) async {
        guard let activity = liveActivity,
              let transfer = transfers.first(where: { $0.id == transferID }) else { return }
        let state = TransferActivityAttributes.ContentState(
            phase: phase,
            completedBytes: transfer.completedBytes,
            totalBytes: transfer.totalBytes)
        await activity.end(ActivityContent(state: state, staleDate: nil), dismissalPolicy: .default)
        liveActivity = nil
        liveActivityStatus = "Live Activity ended."
    }

    private func fileSize(_ url: URL) -> Int64? {
        (try? url.resourceValues(forKeys: [.fileSizeKey]).fileSize).map(Int64.init)
    }

    private func persistentPhotoCopy(for sourceURL: URL) -> URL? {
        let directory = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("PendingUploads", isDirectory: true)
        do {
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            let destination = directory.appendingPathComponent(UUID().uuidString)
                .appendingPathExtension(sourceURL.pathExtension)
            try FileManager.default.copyItem(at: sourceURL, to: destination)
            return destination
        } catch {
            return nil
        }
    }

    private func requestNotificationPermission() {
        Task {
            _ = try? await UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound])
        }
    }

    private func sendTransferNotification(title: String, body: String) {
        let content = UNMutableNotificationContent()
        content.title = title
        content.body = body
        content.sound = .default
        let request = UNNotificationRequest(
            identifier: UUID().uuidString,
            content: content,
            trigger: nil)
        UNUserNotificationCenter.current().add(request)
    }

    func refreshPartialFileSummary() {
        let directories = [
            FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
                .appendingPathComponent("PendingUploads", isDirectory: true),
            FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
                .appendingPathComponent("Downloads", isDirectory: true),
        ]
        let activePaths = Set(jobs.values.compactMap { job -> String? in
            switch job.kind {
            case let .upload(localURL, _, _): return localURL.path
            case let .download(_, partialURL, _): return partialURL.path
            }
        })
        var count = 0
        var bytes: Int64 = 0
        for directory in directories {
            guard let files = try? FileManager.default.contentsOfDirectory(
                at: directory,
                includingPropertiesForKeys: [.fileSizeKey]) else { continue }
            for file in files where !activePaths.contains(file.path) {
                count += 1
                if let size = try? file.resourceValues(forKeys: [.fileSizeKey]).fileSize {
                    bytes += Int64(size ?? 0)
                }
            }
        }
        partialFileCount = count
        partialStorageBytes = bytes
    }

    func clearOrphanedPartialFiles() {
        let directories = [
            FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
                .appendingPathComponent("PendingUploads", isDirectory: true),
            FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
                .appendingPathComponent("Downloads", isDirectory: true),
        ]
        let activePaths = Set(jobs.values.compactMap { job -> String? in
            switch job.kind {
            case let .upload(localURL, _, _): return localURL.path
            case let .download(_, partialURL, _): return partialURL.path
            }
        })
        for directory in directories {
            guard let files = try? FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil) else { continue }
            for file in files where !activePaths.contains(file.path) {
                try? FileManager.default.removeItem(at: file)
            }
        }
        refreshPartialFileSummary()
    }

    func clearTransferHistory() {
        transferHistory.removeAll()
        UserDefaults.standard.removeObject(forKey: historyKey)
    }

    func saveToPhotos(_ transfer: TransferItem) {
        guard transfer.direction == .download,
              case let .download(_, _, finalURL) = jobs[transfer.id]?.kind else { return }
        PHPhotoLibrary.requestAuthorization(for: .addOnly) { [weak self] status in
            guard status == .authorized || status == .limited else { return }
            PHPhotoLibrary.shared().performChanges({
                let videoExtensions = ["mov", "mp4", "m4v", "avi"]
                if videoExtensions.contains(finalURL.pathExtension.lowercased()) {
                    PHAssetChangeRequest.creationRequestForAssetFromVideo(atFileURL: finalURL)
                } else {
                    PHAssetChangeRequest.creationRequestForAssetFromImage(atFileURL: finalURL)
                }
            }) { success, error in
                guard !success, let error else { return }
                Task { @MainActor in self?.errorMessage = error.localizedDescription }
            }
        }
    }

    private func loadHistory() {
        guard let data = UserDefaults.standard.data(forKey: historyKey),
              let history = try? JSONDecoder().decode([TransferHistoryItem].self, from: data) else { return }
        transferHistory = history
    }

    private func recordHistory(for transferID: UUID, result: String) {
        guard let transfer = transfers.first(where: { $0.id == transferID }) else { return }
        transferHistory.insert(TransferHistoryItem(transfer: transfer, result: result), at: 0)
        if transferHistory.count > 100 { transferHistory.removeLast(transferHistory.count - 100) }
        if let data = try? JSONEncoder().encode(transferHistory) {
            UserDefaults.standard.set(data, forKey: historyKey)
        }
    }

    private func cleanupUploadResources(for transferID: UUID) {
        if let cleanupURL = cleanupURLs.removeValue(forKey: transferID) {
            try? FileManager.default.removeItem(at: cleanupURL)
        }
        scopedURLs.removeValue(forKey: transferID)?.stopAccessingSecurityScopedResource()
    }

    private func cleanupDownloadResource(for transferID: UUID) {
        guard let job = jobs[transferID], case let .download(_, partialURL, finalURL) = job.kind else { return }
        try? FileManager.default.removeItem(at: partialURL)
        try? FileManager.default.removeItem(at: finalURL)
    }

    private func restorePersistedUploads() {
        guard let data = UserDefaults.standard.data(forKey: persistedQueueKey),
              let records = try? JSONDecoder().decode([PersistedUpload].self, from: data) else {
            return
        }

        var restoredRecords: [PersistedUpload] = []
        for record in records {
            var isStale = false
            guard let url = try? URL(
                resolvingBookmarkData: record.bookmarkData,
                options: [.withoutUI],
                relativeTo: nil,
                bookmarkDataIsStale: &isStale),
                url.isFileURL,
                FileManager.default.fileExists(atPath: url.path) else {
                continue
            }

            let transfer = TransferItem(
                name: record.name,
                direction: .upload,
                totalBytes: fileSize(url))
            transfers.append(transfer)
            jobs[transfer.id] = TransferJob(
                id: transfer.id,
                kind: .upload(
                    localURL: url,
                    remotePath: record.remotePath,
                    partialRemotePath: record.partialRemotePath ?? "\(record.remotePath).kinodrop-\(transfer.id.uuidString).part"))
            pendingJobs.append(transfer.id)
            if url.startAccessingSecurityScopedResource() {
                scopedURLs[transfer.id] = url
            }
            if isStale, let refreshedData = try? url.bookmarkData(options: []) {
                restoredRecords.append(PersistedUpload(
                    name: record.name,
                    remotePath: record.remotePath,
                    partialRemotePath: record.partialRemotePath,
                    bookmarkData: refreshedData))
            } else {
                restoredRecords.append(record)
            }
        }

        if restoredRecords.isEmpty {
            UserDefaults.standard.removeObject(forKey: persistedQueueKey)
        } else if let data = try? JSONEncoder().encode(restoredRecords) {
            UserDefaults.standard.set(data, forKey: persistedQueueKey)
        }
    }

    private func restorePersistedDownloads() {
        guard let data = UserDefaults.standard.data(forKey: persistedDownloadQueueKey),
              let records = try? JSONDecoder().decode([PersistedDownload].self, from: data) else {
            return
        }

        for record in records {
            let partialURL = URL(fileURLWithPath: record.partialPath)
            guard FileManager.default.fileExists(atPath: partialURL.path) else { continue }
            let transfer = TransferItem(
                name: record.name,
                direction: .download,
                totalBytes: record.totalBytes)
            let finalURL = FileManager.default.temporaryDirectory.appendingPathComponent(record.name)
            transfers.append(transfer)
            jobs[transfer.id] = TransferJob(
                id: transfer.id,
                kind: .download(remotePath: record.remotePath, partialURL: partialURL, finalURL: finalURL))
            pendingJobs.append(transfer.id)
        }
    }

    private func persistQueue() {
        var ids = pendingJobs
        if let activeTransferID { ids.append(activeTransferID) }
        ids.append(contentsOf: transfers.compactMap { transfer in
            transfer.isRetryable ? transfer.id : nil
        })

        var records: [PersistedUpload] = []
        for id in ids.uniqued() {
            guard let job = jobs[id], case let .upload(localURL, remotePath, partialRemotePath) = job.kind,
                  let transfer = transfers.first(where: { $0.id == id }) else { continue }
            switch transfer.state {
            case .queued, .paused, .transferring, .failed:
                guard let bookmarkData = try? localURL.bookmarkData(options: []) else { continue }
                records.append(PersistedUpload(
                    name: transfer.name,
                    remotePath: remotePath,
                    partialRemotePath: partialRemotePath,
                    bookmarkData: bookmarkData))
            case .completed, .cancelled:
                continue
            }
        }

        guard !records.isEmpty, let data = try? JSONEncoder().encode(records) else {
            UserDefaults.standard.removeObject(forKey: persistedQueueKey)
            persistDownloads()
            return
        }
        UserDefaults.standard.set(data, forKey: persistedQueueKey)
        persistDownloads()
    }

    private func persistDownloads() {
        var ids = pendingJobs
        if let activeTransferID { ids.append(activeTransferID) }
        ids.append(contentsOf: transfers.compactMap { transfer in
            transfer.isRetryable ? transfer.id : nil
        })

        let records = ids.uniqued().compactMap { id -> PersistedDownload? in
            guard let job = jobs[id],
                  case let .download(remotePath, partialURL, _) = job.kind,
                  let transfer = transfers.first(where: { $0.id == id }) else { return nil }
            switch transfer.state {
            case .queued, .paused, .transferring, .failed:
                return PersistedDownload(
                    name: transfer.name,
                    remotePath: remotePath,
                    totalBytes: transfer.totalBytes,
                    partialPath: partialURL.path)
            case .completed, .cancelled:
                return nil
            }
        }

        guard !records.isEmpty, let data = try? JSONEncoder().encode(records) else {
            UserDefaults.standard.removeObject(forKey: persistedDownloadQueueKey)
            return
        }
        UserDefaults.standard.set(data, forKey: persistedDownloadQueueKey)
    }
}

private extension Array where Element: Hashable {
    func uniqued() -> [Element] {
        var seen = Set<Element>()
        return filter { seen.insert($0).inserted }
    }
}

private final class ProgressThrottle: @unchecked Sendable {
    private let lock = NSLock()
    private var lastEmission = Date.distantPast

    func shouldEmit() -> Bool {
        lock.lock()
        defer { lock.unlock() }

        let now = Date()
        guard now.timeIntervalSince(lastEmission) >= 0.1 else { return false }
        lastEmission = now
        return true
    }
}
