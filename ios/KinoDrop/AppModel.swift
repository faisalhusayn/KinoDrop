import Foundation
import Photos
import PhotosUI
import SwiftUI

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
    @Published var transfers: [TransferItem] = []
    @Published var remoteFiles: [RemoteFile] = []
    @Published var browsePath = ""
    @Published var errorMessage: String?
    @Published var showQRScanner = false
    @Published var shareURL: URL?

    let smb = SMBClient()
    private let keychain = KeychainStore()
    private var cleanupURLs: [UUID: URL] = [:]

    init() {
        config = keychain.load() ?? .default
    }

    var isConnected: Bool { connectionState == .connected }

    func connect() async {
        errorMessage = nil
        connectionState = .connecting

        do {
            try await smb.connect(using: config)
            try keychain.save(config)
            connectionState = .connected
            await refreshFiles()
        } catch {
            connectionState = .failed(error.localizedDescription)
            errorMessage = error.localizedDescription
        }
    }

    func disconnect() async {
        await smb.disconnect()
        connectionState = .disconnected
        remoteFiles = []
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

    private func enqueueUpload(localURL: URL, remoteName: String) {
        let transfer = TransferItem(
            name: remoteName,
            direction: .upload,
            totalBytes: fileSize(localURL))
        transfers.append(transfer)
        let id = transfer.id
        cleanupURLs[id] = localURL
        Task { await upload(url: localURL, transferID: id, name: remoteName) }
    }

    func importPhotos(_ items: [PhotosPickerItem]) async {
        for item in items {
            guard let data = try? await item.loadTransferable(type: Data.self) else { continue }
            let extensionName = item.supportedContentTypes.first?.preferredFilenameExtension ?? "bin"
            let originalName = photoFilename(for: item)
                ?? "IMG_\(UUID().uuidString).\(extensionName)"
            let url = FileManager.default.temporaryDirectory
                .appendingPathComponent(UUID().uuidString)
                .appendingPathExtension(extensionName)

            do {
                try data.write(to: url, options: .atomic)
                enqueueUpload(localURL: url, remoteName: originalName)
            } catch {
                errorMessage = "Could not prepare a selected photo: \(error.localizedDescription)"
            }
        }
    }

    func importFiles(_ urls: [URL]) async {
        for url in urls {
            let accessed = url.startAccessingSecurityScopedResource()
            defer {
                if accessed { url.stopAccessingSecurityScopedResource() }
            }

            let destination = FileManager.default.temporaryDirectory
                .appendingPathComponent(UUID().uuidString)
                .appendingPathExtension(url.pathExtension)

            do {
                try FileManager.default.copyItem(at: url, to: destination)
                enqueueUpload(localURL: destination, remoteName: url.lastPathComponent)
            } catch {
                errorMessage = "Could not prepare \(url.lastPathComponent): \(error.localizedDescription)"
            }
        }
    }

    private func photoFilename(for item: PhotosPickerItem) -> String? {
        guard let identifier = item.itemIdentifier,
              let asset = PHAsset.fetchAssets(withLocalIdentifiers: [identifier], options: nil).firstObject,
              let resource = PHAssetResource.assetResources(for: asset).first else {
            return nil
        }

        return resource.originalFilename
    }

    private func upload(url: URL, transferID: UUID, name: String) async {
        guard isConnected else {
            updateTransfer(transferID) { $0.state = .failed("Not connected") }
            return
        }

        updateTransfer(transferID) { $0.state = .transferring }
        let remotePath = browsePath.isEmpty ? name : "\(browsePath)/\(name)"
        let progressThrottle = ProgressThrottle()

        do {
            try await smb.upload(localURL: url, remotePath: remotePath) { [weak self] progress in
                guard progressThrottle.shouldEmit() else { return }
                Task { @MainActor in
                    self?.updateTransfer(transferID) {
                        $0.completedBytes = progress.completed
                        if let total = progress.total { $0.totalBytes = total }
                    }
                }
            }
            updateTransfer(transferID) { $0.state = .completed }
        } catch {
            updateTransfer(transferID) { $0.state = .failed(error.localizedDescription) }
        }

        try? FileManager.default.removeItem(at: cleanupURLs.removeValue(forKey: transferID) ?? url)
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
        let progressThrottle = ProgressThrottle()

        Task {
            updateTransfer(id) { $0.state = .transferring }
            do {
                try await smb.download(remotePath: file.path, localURL: destination) { [weak self] progress in
                    guard progressThrottle.shouldEmit() else { return }
                    Task { @MainActor in
                        self?.updateTransfer(id) {
                            $0.completedBytes = progress.completed
                            if let total = progress.total { $0.totalBytes = total }
                        }
                    }
                }
                updateTransfer(id) { $0.state = .completed }
                shareURL = destination
            } catch {
                updateTransfer(id) { $0.state = .failed(error.localizedDescription) }
                try? FileManager.default.removeItem(at: destination)
            }
        }
    }

    private func updateTransfer(_ id: UUID, update: (inout TransferItem) -> Void) {
        guard let index = transfers.firstIndex(where: { $0.id == id }) else { return }
        update(&transfers[index])
    }

    private func fileSize(_ url: URL) -> Int64? {
        (try? url.resourceValues(forKeys: [.fileSizeKey]).fileSize).map(Int64.init)
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
