import ActivityKit

public struct TransferActivityAttributes: ActivityAttributes {
    public struct ContentState: Codable, Hashable {
        public var phase: String
        public var completedBytes: Int64
        public var totalBytes: Int64?

        public init(phase: String, completedBytes: Int64, totalBytes: Int64?) {
            self.phase = phase
            self.completedBytes = completedBytes
            self.totalBytes = totalBytes
        }

        public var progress: Double? {
            guard let totalBytes, totalBytes > 0 else { return nil }
            return min(Double(completedBytes) / Double(totalBytes), 1)
        }
    }

    public let fileName: String
    public let direction: String

    public init(fileName: String, direction: String) {
        self.fileName = fileName
        self.direction = direction
    }
}
