import Foundation
import Network

struct NearbyDevice: Identifiable, Equatable {
    let id: String
    let name: String
    let host: String
    let share: String
}

@MainActor
final class NearbyDeviceBrowser {
    private var browser: NWBrowser?
    var onChange: (([NearbyDevice]) -> Void)?

    func start() {
        stop()
        let browser = NWBrowser(for: .bonjour(type: "_kinodrop._tcp", domain: "local."), using: .tcp)
        browser.stateUpdateHandler = { state in
            if case .failed = state { browser.cancel() }
        }
        browser.browseResultsChangedHandler = { [weak self] results, _ in
            let devices = results.compactMap { result -> NearbyDevice? in
                guard case let .service(name, _, _, _) = result.endpoint else { return nil }
                return NearbyDevice(id: name, name: name, host: "\(name).local", share: "KinoShare")
            }
            Task { @MainActor in
                self?.onChange?(devices.sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending })
            }
        }
        browser.start(queue: .main)
        self.browser = browser
    }

    func stop() {
        browser?.cancel()
        browser = nil
    }
}
