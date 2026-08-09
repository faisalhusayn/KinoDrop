import AVFoundation
import Foundation
import UniformTypeIdentifiers

final class SMBVideoResourceLoader: NSObject, AVAssetResourceLoaderDelegate {
    private let client: SMBClient
    private let file: RemoteFile
    private var loadingTasks: [ObjectIdentifier: Task<Void, Never>] = [:]

    init(client: SMBClient, file: RemoteFile) {
        self.client = client
        self.file = file
    }

    func resourceLoader(
        _ resourceLoader: AVAssetResourceLoader,
        shouldWaitForLoadingOfRequestedResource loadingRequest: AVAssetResourceLoadingRequest
    ) -> Bool {
        let requestID = ObjectIdentifier(loadingRequest)
        loadingTasks[requestID] = Task { [weak self, weak loadingRequest] in
            guard let self, let loadingRequest else { return }
            do {
                if let information = loadingRequest.contentInformationRequest {
                    information.contentType = self.contentType
                    information.contentLength = self.file.size ?? 0
                    information.isByteRangeAccessSupported = true
                }

                if let dataRequest = loadingRequest.dataRequest {
                    let offset = max(dataRequest.currentOffset, dataRequest.requestedOffset)
                    let length = dataRequest.requestedLength
                    let data = try await self.client.read(
                        remotePath: self.file.path,
                        offset: offset,
                        length: length)
                    dataRequest.respond(with: data)
                }
                loadingRequest.finishLoading()
            } catch is CancellationError {
                loadingRequest.finishLoading()
            } catch {
                loadingRequest.finishLoading(with: error)
            }
            self.loadingTasks[requestID] = nil
        }
        return true
    }

    func resourceLoader(
        _ resourceLoader: AVAssetResourceLoader,
        didCancel loadingRequest: AVAssetResourceLoadingRequest
    ) {
        loadingTasks.removeValue(forKey: ObjectIdentifier(loadingRequest))?.cancel()
    }

    private var contentType: String {
        let fileExtension = (file.name as NSString).pathExtension
        return UTType(filenameExtension: fileExtension)?.identifier ?? AVFileType.mp4.rawValue
    }
}
