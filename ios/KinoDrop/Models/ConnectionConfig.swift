import Foundation

struct ConnectionConfig: Codable, Equatable {
    var host: String
    var share: String
    var username: String
    var password: String

    static let `default` = ConnectionConfig(
        host: "",
        share: "KinoShare",
        username: "kinoshare",
        password: "")

    var serverURL: URL? {
        guard !host.isEmpty else { return nil }
        return URL(string: "smb://\(host)")
    }
}
