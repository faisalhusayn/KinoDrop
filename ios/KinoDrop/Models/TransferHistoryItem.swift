import Foundation

struct TransferHistoryItem: Identifiable, Codable {
    let id: UUID
    let name: String
    let direction: TransferItem.Direction
    let bytes: Int64
    let date: Date
    let result: String

    init(transfer: TransferItem, result: String) {
        id = UUID()
        name = transfer.name
        direction = transfer.direction
        bytes = transfer.completedBytes
        date = Date()
        self.result = result
    }
}
