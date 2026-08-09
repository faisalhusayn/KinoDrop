import Foundation

struct RemoteFile: Identifiable, Hashable {
    let id: String
    let name: String
    let path: String
    let isDirectory: Bool
    let size: Int64?
    let modified: Date?

    var isPreviewable: Bool {
        guard !isDirectory else { return false }
        let previewExtensions = [
            "avif", "bmp", "gif", "heic", "heif", "jpeg", "jpg", "m4v", "mov", "mp4", "png", "tif", "tiff", "webm"
        ]
        return previewExtensions.contains((name as NSString).pathExtension.lowercased())
    }
}
