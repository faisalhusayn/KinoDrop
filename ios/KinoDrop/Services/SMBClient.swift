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
        offset: Int64 = 0,
        progress: @escaping @Sendable (SMBProgress) -> Bool) async throws {
        guard let manager else { throw SMBClientError.notConnected }
        try await manager.uploadItemPipelined(
            at: localURL,
            toPath: remotePath,
            chunkSize: 1_048_576,
            pipelineSize: 16,
            offset: offset
        ) { completed in
            return progress(SMBProgress(completed: completed, total: nil))
        }
    }

    func remoteFileSize(at remotePath: String) async throws -> Int64? {
        let components = remotePath.split(separator: "/")
        let name = String(components.last ?? "")
        let parent = components.dropLast().joined(separator: "/")
        return try await listDirectory(path: parent).first(where: { $0.name == name })?.size
    }

    func move(remotePath: String, to destinationPath: String) async throws {
        guard let manager else { throw SMBClientError.notConnected }
        try await manager.moveItem(atPath: remotePath, toPath: destinationPath)
    }

    func remove(remotePath: String) async throws {
        guard let manager else { throw SMBClientError.notConnected }
        try await manager.removeItem(atPath: remotePath)
    }

    func download(
        remotePath: String,
        localURL: URL,
        progress: @escaping @Sendable (SMBProgress) -> Bool) async throws {
        guard let manager else { throw SMBClientError.notConnected }
        try await manager.downloadItem(atPath: remotePath, to: localURL) { completed, total in
            return progress(SMBProgress(completed: completed, total: total))
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
