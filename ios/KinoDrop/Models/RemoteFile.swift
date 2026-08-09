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
            "avif", "bmp", "gif", "heic", "heif", "jpeg", "jpg", "png", "tif", "tiff"
        ]
        return previewExtensions.contains((name as NSString).pathExtension.lowercased())
    }

    var isVideo: Bool {
        guard !isDirectory else { return false }
        return ["avi", "m4v", "mkv", "mov", "mp4", "mpeg", "mpg", "webm"].contains(
            (name as NSString).pathExtension.lowercased())
    }

    var isAudio: Bool {
        guard !isDirectory else { return false }
        return ["aac", "aiff", "flac", "m4a", "mp3", "wav"].contains(
            (name as NSString).pathExtension.lowercased())
    }

    var isImage: Bool {
        guard !isDirectory else { return false }
        return ["avif", "bmp", "gif", "heic", "heif", "jpeg", "jpg", "png", "tif", "tiff"].contains(
            (name as NSString).pathExtension.lowercased())
    }

    var isPDF: Bool {
        !isDirectory && (name as NSString).pathExtension.lowercased() == "pdf"
    }

    var isText: Bool {
        guard !isDirectory else { return false }
        return ["csv", "json", "log", "md", "rtf", "text", "txt", "xml", "yaml", "yml"].contains(
            (name as NSString).pathExtension.lowercased())
    }

    var canPreview: Bool {
        isVideo || isAudio || isImage || isPDF || isText
    }
}
