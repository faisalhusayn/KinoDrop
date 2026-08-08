import Foundation

struct TransferItem: Identifiable {
    enum Direction {
        case upload
        case download
    }

    enum State {
        case queued
        case transferring
        case completed
        case failed(String)
        case cancelled
    }

    let id = UUID()
    let name: String
    let direction: Direction
    var completedBytes: Int64 = 0
    var totalBytes: Int64?
    var preparationDuration: TimeInterval?
    var transferDuration: TimeInterval?
    var state: State = .queued

    var progress: Double? {
        guard let totalBytes, totalBytes > 0 else { return nil }
        return min(Double(completedBytes) / Double(totalBytes), 1)
    }
}
