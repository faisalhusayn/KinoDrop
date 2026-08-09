import Foundation
import ActivityKit
import PhotosUI
import SwiftUI
import UIKit

private struct PersistedUpload: Codable {
    let name: String
    let remotePath: String
    let bookmarkData: Data
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

    let smb = SMBClient()
    private let keychain = KeychainStore()
    private var cleanupURLs: [UUID: URL] = [:]
    private var scopedURLs: [UUID: URL] = [:]
    private enum TransferKind {
        case upload(localURL: URL, remotePath: String)
        case download(remotePath: String, localURL: URL)
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
    private var liveActivity: Activity<TransferActivityAttributes>?

    init() {
        config = keychain.load() ?? .default
        restorePersistedUploads()
    }

    var isConnected: Bool { connectionState == .connected }

    func connect() async {
        errorMessage = nil
        connectionState = .connecting

        do {
            try await smb.connect(using: config)
            try keychain.save(config)
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
            pauseRequested = false
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
        jobs[id] = TransferJob(id: id, kind: .upload(localURL: localURL, remotePath: remotePath))
        pendingJobs.append(id)
        persistQueue()
        startNextTransfer()
    }

    func importPhotos(_ items: [PhotosPickerItem]) async {
        let preparationStart = Date()
        for item in items {
            guard let picked = try? await item.loadTransferable(type: PickedFile.self) else { continue }
            let preparationDuration = Date().timeIntervalSince(preparationStart)
            enqueueUpload(
                localURL: picked.url,
                remoteName: picked.filename,
                preparationDuration: preparationDuration,
                cleanup: true)
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
        updateTransfer(job.id) { $0.state = .transferring }
        if let transfer = transfers.first(where: { $0.id == job.id }) {
            await beginLiveActivity(for: transfer)
        }
        let progressThrottle = ProgressThrottle()
        let transferStart = Date()

        do {
            var attempt = 0
            while true {
                do {
                    try await execute(job, progressThrottle: progressThrottle)
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
                $0.state = .completed
            }
            if case let .download(_, localURL) = job.kind { shareURL = localURL }
            cleanupUploadResources(for: job.id)
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
            await endLiveActivity(phase: wasPaused ? "Paused" : "Stopped", transferID: job.id)
        }

        activeTransferID = nil
        activeTask = nil
        persistQueue()
        startNextTransfer()
    }

    private func execute(_ job: TransferJob, progressThrottle: ProgressThrottle) async throws {
        switch job.kind {
        case let .upload(localURL, remotePath):
            try await smb.upload(localURL: localURL, remotePath: remotePath) { [weak self] progress in
                guard !Task.isCancelled else { return false }
                if progressThrottle.shouldEmit() {
                    Task { @MainActor [weak self] in
                        self?.updateTransferProgress(job.id, progress: progress)
                    }
                }
                return true
            }
        case let .download(remotePath, localURL):
            try await smb.download(remotePath: remotePath, localURL: localURL) { [weak self] progress in
                guard !Task.isCancelled else { return false }
                if progressThrottle.shouldEmit() {
                    Task { @MainActor [weak self] in
                        self?.updateTransferProgress(job.id, progress: progress)
                    }
                }
                return true
            }
        }
    }

    func download(_ file: RemoteFile) {
        let destination = FileManager.default.temporaryDirectory
            .appendingPathComponent(file.name)
        try? FileManager.default.removeItem(at: destination)

        let transfer = TransferItem(
            name: file.name,
            direction: .download,
            totalBytes: file.size)
        transfers.append(transfer)
        let id = transfer.id
        jobs[id] = TransferJob(id: id, kind: .download(remotePath: file.path, localURL: destination))
        pendingJobs.append(id)
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
            $0.completedBytes = progress.completed
            if let total = progress.total { $0.totalBytes = total }
        }
        Task { @MainActor [weak self] in
            await self?.updateLiveActivity(transferID: id)
        }
    }

    private func beginLiveActivity(for transfer: TransferItem) async {
        guard ActivityAuthorizationInfo().areActivitiesEnabled else { return }
        let attributes = TransferActivityAttributes(
            fileName: transfer.name,
            direction: transfer.direction == .upload ? "upload" : "download")
        let state = TransferActivityAttributes.ContentState(
            phase: transfer.direction == .upload ? "Uploading" : "Downloading",
            completedBytes: transfer.completedBytes,
            totalBytes: transfer.totalBytes)
        liveActivity = try? await Activity.request(
            attributes: attributes,
            content: ActivityContent(state: state, staleDate: nil),
            pushType: nil)
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
    }

    private func fileSize(_ url: URL) -> Int64? {
        (try? url.resourceValues(forKeys: [.fileSizeKey]).fileSize).map(Int64.init)
    }

    private func cleanupUploadResources(for transferID: UUID) {
        if let cleanupURL = cleanupURLs.removeValue(forKey: transferID) {
            try? FileManager.default.removeItem(at: cleanupURL)
        }
        scopedURLs.removeValue(forKey: transferID)?.stopAccessingSecurityScopedResource()
    }

    private func cleanupDownloadResource(for transferID: UUID) {
        guard let job = jobs[transferID], case let .download(_, localURL) = job.kind else { return }
        try? FileManager.default.removeItem(at: localURL)
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
                options: [.withSecurityScope, .withoutUI],
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
                kind: .upload(localURL: url, remotePath: record.remotePath))
            pendingJobs.append(transfer.id)
            if url.startAccessingSecurityScopedResource() {
                scopedURLs[transfer.id] = url
            }
            if isStale, let refreshedData = try? url.bookmarkData(options: []) {
                restoredRecords.append(PersistedUpload(
                    name: record.name,
                    remotePath: record.remotePath,
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

    private func persistQueue() {
        var ids = pendingJobs
        if let activeTransferID { ids.append(activeTransferID) }
        ids.append(contentsOf: transfers.compactMap { transfer in
            transfer.isRetryable ? transfer.id : nil
        })

        var records: [PersistedUpload] = []
        for id in ids.uniqued() {
            guard let job = jobs[id], case let .upload(localURL, remotePath) = job.kind,
                  let transfer = transfers.first(where: { $0.id == id }) else { continue }
            switch transfer.state {
            case .queued, .paused, .transferring, .failed:
                guard let bookmarkData = try? localURL.bookmarkData(options: []) else { continue }
                records.append(PersistedUpload(
                    name: transfer.name,
                    remotePath: remotePath,
                    bookmarkData: bookmarkData))
            case .completed, .cancelled:
                continue
            }
        }

        guard !records.isEmpty, let data = try? JSONEncoder().encode(records) else {
            UserDefaults.standard.removeObject(forKey: persistedQueueKey)
            return
        }
        UserDefaults.standard.set(data, forKey: persistedQueueKey)
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
