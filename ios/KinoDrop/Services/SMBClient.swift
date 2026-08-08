import AMSMB2
import Foundation
import os

struct SMBProgress {
    let completed: Int64
    let total: Int64?
}

struct SMBConnectionDiagnostics: Equatable {
    let dialect: String
    let maxWriteSize: Int
}

final class SMBClient {
    private var manager: SMB2Manager?
    private let logger = Logger(subsystem: "com.faisalhusayn.kinodrop", category: "SMB")

    var isConnected: Bool { manager != nil }

    var diagnostics: SMBConnectionDiagnostics? {
        guard let manager else { return nil }
        return SMBConnectionDiagnostics(
            dialect: manager.negotiatedDialect,
            maxWriteSize: manager.negotiatedMaxWriteSize)
    }

    func connect(using config: ConnectionConfig) async throws {
        guard let serverURL = config.serverURL else {
            throw SMBClientError.invalidAddress
        }

        let credential = URLCredential(
            user: config.username,
            password: config.password,
            persistence: .forSession)

        guard let manager = SMB2Manager(url: serverURL, credential: credential) else {
            throw SMBClientError.invalidAddress
        }

        try await manager.connectShare(name: config.share)
        self.manager = manager
        logger.info(
            "Connected with SMB dialect \(manager.negotiatedDialect, privacy: .public), max write size \(manager.negotiatedMaxWriteSize, privacy: .public) bytes"
        )
    }

    func disconnect() async {
        try? await manager?.disconnectShare()
        manager = nil
    }

    func listDirectory(path: String) async throws -> [RemoteFile] {
        guard let manager else { throw SMBClientError.notConnected }

        let entries = try await manager.contentsOfDirectory(atPath: path)
        return entries.compactMap { entry in
            guard let name = entry[.nameKey] as? String else { return nil }
            let childPath = path.isEmpty ? name : "\(path)/\(name)"
            let fileType = entry[.fileResourceTypeKey] as? URLFileResourceType
            let isDirectory = fileType == .directory
            let size = entry[.fileSizeKey] as? Int64
            let modified = entry[.contentModificationDateKey] as? Date
            return RemoteFile(
                id: childPath,
                name: name,
                path: childPath,
                isDirectory: isDirectory,
                size: size,
                modified: modified)
        }
        .sorted {
            if $0.isDirectory != $1.isDirectory { return $0.isDirectory }
            return $0.name.localizedStandardCompare($1.name) == .orderedAscending
        }
    }

    func upload(
        localURL: URL,
        remotePath: String,
        progress: @escaping @Sendable (SMBProgress) -> Void) async throws {
        guard let manager else { throw SMBClientError.notConnected }
        try await manager.uploadItemPipelined(
            at: localURL,
            toPath: remotePath,
            chunkSize: 2_097_152,
            pipelineSize: 8
        ) { completed in
            progress(SMBProgress(completed: completed, total: nil))
            return true
        }
    }

    func download(
        remotePath: String,
        localURL: URL,
        progress: @escaping @Sendable (SMBProgress) -> Void) async throws {
        guard let manager else { throw SMBClientError.notConnected }
        try await manager.downloadItem(atPath: remotePath, to: localURL) { completed, total in
            progress(SMBProgress(completed: completed, total: total))
            return true
        }
    }
}

enum SMBClientError: LocalizedError {
    case invalidAddress
    case notConnected

    var errorDescription: String? {
        switch self {
        case .invalidAddress:
            return "Enter a valid PC address or scan the KinoDrop QR code."
        case .notConnected:
            return "KinoDrop is not connected to the PC."
        }
    }
}
