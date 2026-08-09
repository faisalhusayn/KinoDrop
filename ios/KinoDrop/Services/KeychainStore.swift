import Foundation
import Security

struct KeychainStore {
    private let service = "com.faisalhusayn.kinodrop"
    private let account = "connections"
    private let legacyAccount = "connection"

    func load() -> ConnectionConfig? {
        guard let data = loadData(account: account) else {
            return loadData(account: legacyAccount).flatMap { try? JSONDecoder().decode(ConnectionConfig.self, from: $0) }
        }
        return (try? JSONDecoder().decode([SavedConnection].self, from: data))?.first?.config
    }

    func loadConnections() -> [SavedConnection] {
        if let data = loadData(account: account),
           let connections = try? JSONDecoder().decode([SavedConnection].self, from: data) {
            return connections
        }

        guard let legacy = loadData(account: legacyAccount),
              let config = try? JSONDecoder().decode(ConnectionConfig.self, from: legacy) else {
            return []
        }
        let migrated = [SavedConnection(name: config.host, config: config)]
        try? saveConnections(migrated)
        return migrated
    }

    @discardableResult
    func saveConnection(_ config: ConnectionConfig, name: String? = nil) throws -> [SavedConnection] {
        var connections = loadConnections()
        if let index = connections.firstIndex(where: { $0.config.host.caseInsensitiveCompare(config.host) == .orderedSame }) {
            connections[index].config = config
            if let name, !name.isEmpty { connections[index].name = name }
        } else {
            let connectionName = name?.isEmpty == false ? name! : config.host
            connections.insert(SavedConnection(name: connectionName, config: config), at: 0)
        }
        try saveConnections(connections)
        return connections
    }

    func deleteConnection(_ id: UUID) throws {
        try saveConnections(loadConnections().filter { $0.id != id })
    }

    private func loadData(account: String) -> Data? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne
        ]

        var result: CFTypeRef?
        return SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess ? result as? Data : nil
    }

    func save(_ config: ConnectionConfig) throws {
        _ = try saveConnection(config)
    }

    private func saveConnections(_ connections: [SavedConnection]) throws {
        let data = try JSONEncoder().encode(connections)
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account
        ]
        let attributes: [String: Any] = [kSecValueData as String: data]

        let status = SecItemUpdate(query as CFDictionary, attributes as CFDictionary)
        if status == errSecItemNotFound {
            var item = query
            item[kSecValueData as String] = data
            guard SecItemAdd(item as CFDictionary, nil) == errSecSuccess else {
                throw KeychainError.saveFailed
            }
        } else if status != errSecSuccess {
            throw KeychainError.saveFailed
        }
    }
}

enum KeychainError: LocalizedError {
    case saveFailed

    var errorDescription: String? {
        "Could not save the KinoDrop connection securely."
    }
}
