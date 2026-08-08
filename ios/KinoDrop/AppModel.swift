import Foundation
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
            let name = url.lastPathComponent
            let transfer = TransferItem(name: name, direction: .upload, totalBytes: fileSize(url))
            transfers.append(transfer)
            let id = transfers[transfers.count - 1].id
            cleanupURLs[id] = url
            Task { await upload(url: url, transferID: id, name: name) }
        }
    }

    func importPhotos(_ items: [PhotosPickerItem]) async {
        var urls: [URL] = []
        for item in items {
            guard let data = try? await item.loadTransferable(type: Data.self) else { continue }
            let extensionName = item.supportedContentTypes.first?.preferredFilenameExtension ?? "bin"
            let url = FileManager.default.temporaryDirectory
                .appendingPathComponent(UUID().uuidString)
                .appendingPathExtension(extensionName)

            do {
                try data.write(to: url, options: .atomic)
                urls.append(url)
            } catch {
                errorMessage = "Could not prepare a selected photo: \(error.localizedDescription)"
            }
        }

        enqueueUploads(urls: urls)
    }

    func importFiles(_ urls: [URL]) async {
        var localURLs: [URL] = []
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
                localURLs.append(destination)
            } catch {
                errorMessage = "Could not prepare \(url.lastPathComponent): \(error.localizedDescription)"
            }
        }

        enqueueUploads(urls: localURLs)
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
