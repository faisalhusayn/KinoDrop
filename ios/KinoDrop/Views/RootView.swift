import PhotosUI
import Foundation
import SwiftUI
import UIKit
import UniformTypeIdentifiers

struct RootView: View {
    @ObservedObject var model: AppModel

    var body: some View {
        Group {
            if model.isConnected {
                HomeView(model: model)
            } else {
                ConnectView(model: model)
            }
        }
        .sheet(isPresented: $model.showQRScanner) {
            QRScannerView(
                onCode: { url in
                    model.applyScannedURL(url)
                    model.showQRScanner = false
                },
                onCancel: { model.showQRScanner = false })
            .ignoresSafeArea()
        }
    }
}

struct ConnectView: View {
    @ObservedObject var model: AppModel

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    Image("KinoDropLogo")
                        .resizable()
                        .scaledToFit()
                        .frame(maxWidth: .infinity)
                        .frame(height: 150)
                        .clipShape(RoundedRectangle(cornerRadius: 18))
                        .accessibilityLabel("KinoDrop logo")
                }

                Section {
                    Text("Connect to the KinoDrop share running on your Windows PC.")
                        .foregroundStyle(.secondary)

                    TextField("PC address", text: $model.config.host)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()

                    TextField("Share", text: $model.config.share)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()

                    TextField("Username", text: $model.config.username)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()

                    SecureField("Password", text: $model.config.password)
                } header: {
                    Text("Connection")
                }

                Section {
                    if !model.nearbyDevices.isEmpty {
                        ForEach(model.nearbyDevices) { device in
                            Button {
                                model.useNearbyDevice(device)
                            } label: {
                                Label(device.name, systemImage: "desktopcomputer.and.arrow.down")
                            }
                        }
                    }

                    Button {
                        model.showQRScanner = true
                    } label: {
                        Label("Scan KinoDrop QR code", systemImage: "qrcode.viewfinder")
                    }

                    if model.config.password.isEmpty {
                        Text("First connection? Scan the QR code shown by the Windows app to fill in the secure credentials.")
                            .font(.footnote)
                            .foregroundStyle(.secondary)
                    }

                    Button {
                        Task { await model.connect() }
                    } label: {
                        if model.connectionState == .connecting {
                            ProgressView()
                        } else {
                            Label("Connect", systemImage: "bolt.horizontal.circle.fill")
                        }
                    }
                    .disabled(model.config.host.isEmpty || model.config.password.isEmpty)
                }

                Section("Nearby KinoDrop PCs") {
                    if model.nearbyDevices.isEmpty {
                        Text("Searching for KinoDrop PCs on this local network...")
                            .font(.footnote)
                            .foregroundStyle(.secondary)
                    } else {
                        Text("Tap a PC above to fill its address. Scan the QR code for first-time credentials.")
                            .font(.footnote)
                            .foregroundStyle(.secondary)
                    }
                    Button {
                        model.refreshNearbyDevices()
                    } label: {
                        Label("Search again", systemImage: "arrow.clockwise")
                    }
                }

                if let error = model.errorMessage {
                    Section {
                        Text(error)
                            .foregroundStyle(.red)
                    }
                }
            }
            .navigationTitle("KinoDrop")
        }
    }
}

struct HomeView: View {
    @ObservedObject var model: AppModel
    @State private var photoItems: [PhotosPickerItem] = []
    @State private var showFilePicker = false
    @State private var historySearch = ""
    @State private var showTransfers = true
    @State private var showBrowse = true
    @State private var showHistory = false

    var body: some View {
        NavigationStack {
            List {
                Section {
                    VStack(alignment: .leading, spacing: 6) {
                        Label("Connected", systemImage: "checkmark.circle.fill")
                            .font(.headline)
                            .foregroundStyle(.green)
                        Text("\(model.config.host) / \(model.config.share)")
                            .font(.subheadline)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }
                    .padding(.vertical, 4)
                }

                Section {
                    PhotosPicker(
                        selection: $photoItems,
                        maxSelectionCount: 50,
                        matching: .any(of: [.images, .videos])) {
                            Label("Photos and videos", systemImage: "photo.on.rectangle.angled")
                        }
                        .onChange(of: photoItems) { _, items in
                            Task {
                                await model.importPhotos(items)
                                photoItems = []
                            }
                        }

                    Button {
                        showFilePicker = true
                    } label: {
                        Label("Files", systemImage: "doc.badge.plus")
                    }
                } header: {
                    Text("Send to PC")
                } footer: {
                    Text("Files are sent one at a time so the connection stays predictable. Keep KinoDrop open for maximum speed; iOS may reduce transfer speed when the app is in the background.")
                }

                Section {
                    DisclosureGroup(isExpanded: $showTransfers) {
                        if model.transfers.isEmpty {
                            Label("Nothing transferred yet", systemImage: "tray")
                                .foregroundStyle(.secondary)
                        } else {
                            ForEach(model.transfers.reversed()) { transfer in
                                TransferRow(transfer: transfer, model: model)
                            }
                            if model.transfers.contains(where: { transfer in
                                switch transfer.state {
                                case .completed, .cancelled: return true
                                default: return false
                                }
                            }) {
                                Button("Clear completed", role: .destructive) {
                                    model.clearFinishedTransfers()
                                }
                            }
                        }
                    } label: {
                        HStack {
                            Text("Transfers")
                            Spacer()
                            Text("1 active at a time")
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                    }
                }

                Section {
                    DisclosureGroup(isExpanded: $showBrowse) {
                        FileBrowserView(model: model)
                    } label: {
                        Label("Browse", systemImage: "folder")
                    }
                }

                Section {
                    DisclosureGroup(isExpanded: $showHistory) {
                        let visibleHistory = model.transferHistory.filter {
                            historySearch.isEmpty || $0.name.localizedCaseInsensitiveContains(historySearch)
                        }
                        if visibleHistory.isEmpty {
                            Text("No matching transfers")
                                .foregroundStyle(.secondary)
                        } else {
                            ForEach(visibleHistory.prefix(10)) { item in
                                HStack {
                                    Image(systemName: item.direction == .upload ? "arrow.up.circle" : "arrow.down.circle")
                                    VStack(alignment: .leading) {
                                        Text(item.name).lineLimit(1)
                                        Text("\(item.result) · \(ByteCountFormatter.string(fromByteCount: item.bytes, countStyle: .file))")
                                            .font(.caption)
                                            .foregroundStyle(.secondary)
                                    }
                                    Spacer()
                                    Text(item.date, style: .relative)
                                        .font(.caption2)
                                        .foregroundStyle(.secondary)
                                }
                            }
                            Button("Clear history", role: .destructive) {
                                model.clearTransferHistory()
                            }
                        }
                    } label: {
                        Label("History", systemImage: "clock.arrow.circlepath")
                    }
                }
                .searchable(text: $historySearch, prompt: "Search transfer history")

                if let diagnostics = model.smbDiagnostics {
                    Section("Connection details") {
                        LabeledContent("SMB dialect", value: diagnostics.dialect)
                        LabeledContent("Max write", value: "\(diagnostics.maxWriteSize / 1_048_576) MB")
                        if let liveActivityStatus = model.liveActivityStatus {
                            LabeledContent("Live Activity", value: liveActivityStatus)
                        }
                    }
                }

                if model.partialFileCount > 0 {
                    Section("Retained transfer files") {
                        Text("\(model.partialFileCount) file(s), \(ByteCountFormatter.string(fromByteCount: model.partialStorageBytes, countStyle: .file))")
                            .foregroundStyle(.secondary)
                        Button("Clear orphaned files", role: .destructive) {
                            model.clearOrphanedPartialFiles()
                        }
                    }
                }

                Section {
                    Button("Disconnect", role: .destructive) {
                        Task { await model.disconnect() }
                    }
                }
            }
            .navigationTitle("KinoDrop")
            .fileImporter(
                isPresented: $showFilePicker,
                allowedContentTypes: [.data],
                allowsMultipleSelection: true) { result in
                    guard case .success(let urls) = result else { return }
                    Task { await model.importFiles(urls) }
                }
            .sheet(isPresented: Binding(
                get: { model.shareURL != nil },
                set: { if !$0 { model.shareURL = nil } })) {
                    if let url = model.shareURL {
                        ShareSheet(items: [url])
                    }
                }
            .confirmationDialog(
                "\(model.conflictRequest?.name ?? "File") already exists",
                isPresented: Binding(
                    get: { model.conflictRequest != nil },
                    set: { if !$0 { model.resolveConflict(.skip) } })) {
                Button("Overwrite") { model.resolveConflict(.overwrite) }
                Button("Rename copy") { model.resolveConflict(.rename) }
                Button("Skip", role: .cancel) { model.resolveConflict(.skip) }
            } message: {
                Text("Choose what KinoDrop should do with the existing file.")
            }
        }
    }
}

struct ShareSheet: UIViewControllerRepresentable {
    let items: [Any]

    func makeUIViewController(context: Context) -> UIActivityViewController {
        UIActivityViewController(activityItems: items, applicationActivities: nil)
    }

    func updateUIViewController(_ uiViewController: UIActivityViewController, context: Context) {}
}

struct TransferRow: View {
    let transfer: TransferItem
    @ObservedObject var model: AppModel

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Image(systemName: iconName)
                    .foregroundStyle(iconColor)
                Text(transfer.name)
                    .lineLimit(1)
                Spacer()
                Text(stateText)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            if case .completed = transfer.state {
                ProgressView(value: 1)
                HStack {
                    Text("100%")
                    Spacer()
                    if let total = transfer.totalBytes {
                        Text(formatBytes(total))
                    }
                }
                .font(.caption2)
                .foregroundStyle(.secondary)
            } else if let progress = transfer.progress {
                ProgressView(value: progress)
                HStack {
                    Text("\(Int(progress * 100))%")
                    Spacer()
                    if let total = transfer.totalBytes {
                        Text("\(formatBytes(transfer.completedBytes)) of \(formatBytes(total))")
                    }
                }
                .font(.caption2)
                .foregroundStyle(.secondary)
            } else if case .transferring = transfer.state {
                ProgressView()
            }

            HStack {
                if let bytesPerSecond = transfer.bytesPerSecond, bytesPerSecond > 0 {
                    Text("\(formatBytes(Int64(bytesPerSecond)))/s")
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                }
                Spacer()
                if case .failed = transfer.state {
                    Button("Retry") { model.retry(transfer) }
                        .buttonStyle(.bordered)
                }
                if case .queued = transfer.state {
                    Text("Waiting for the active transfer")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                if case .paused = transfer.state {
                    Text("Paused until KinoDrop is active")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                if case .transferring = transfer.state {
                    Button("Cancel", role: .destructive) { model.cancel(transfer) }
                        .buttonStyle(.bordered)
                }
                if transfer.direction == .download, case .completed = transfer.state {
                    Button("Save to Photos") { model.saveToPhotos(transfer) }
                        .buttonStyle(.bordered)
                }
            }
        }
        .padding(.vertical, 4)
    }

    private var stateText: String {
        switch transfer.state {
        case .queued: return "Queued"
        case .paused: return "Paused"
        case .transferring: return transfer.direction == .upload ? "Uploading" : "Downloading"
        case .completed:
            if let duration = transfer.transferDuration {
                return "Done · \(String(format: "%.1f", duration))s"
            }
            return "Done"
        case .failed(let message): return message
        case .cancelled: return "Cancelled"
        }
    }

    private var iconName: String {
        transfer.direction == .upload ? "arrow.up.circle.fill" : "arrow.down.circle.fill"
    }

    private var iconColor: Color {
        transfer.direction == .upload ? .blue : .orange
    }

    private func formatBytes(_ bytes: Int64) -> String {
        ByteCountFormatter.string(fromByteCount: bytes, countStyle: .file)
    }
}

struct FileBrowserView: View {
    @ObservedObject var model: AppModel
    @State private var searchText = ""
    @State private var sortMode = SortMode.name

    private enum SortMode: String, CaseIterable {
        case name = "Name"
        case date = "Date"
        case size = "Size"
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                if !model.browsePath.isEmpty {
                    Button {
                        Task { await model.goUp() }
                    } label: {
                        Label("Up", systemImage: "arrow.up")
                    }
                }
                Text(model.browsePath.isEmpty ? "KinoShare" : model.browsePath)
                    .font(.subheadline)
                    .lineLimit(1)
                Spacer()
                Menu {
                    Picker("Sort by", selection: $sortMode) {
                        ForEach(SortMode.allCases, id: \.self) { mode in
                            Text(mode.rawValue).tag(mode)
                        }
                    }
                } label: {
                    Image(systemName: "arrow.up.arrow.down")
                }
                Button {
                    Task { await model.refreshFiles() }
                } label: {
                    Image(systemName: "arrow.clockwise")
                }
            }

            if model.remoteFiles.isEmpty {
                Text("No files in this folder")
                    .foregroundStyle(.secondary)
            } else {
                ForEach(sortedFiles) { file in
                    Button {
                        if file.isDirectory {
                            Task { await model.open(file) }
                        } else {
                            model.download(file)
                        }
                    } label: {
                        Label(file.name, systemImage: file.isDirectory ? "folder" : "doc")
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                    .buttonStyle(.plain)
                }
            }
        }
        .task { await model.refreshFiles() }
        .searchable(text: $searchText, prompt: "Search files")
    }

    private var sortedFiles: [RemoteFile] {
        model.remoteFiles
            .filter { searchText.isEmpty || $0.name.localizedCaseInsensitiveContains(searchText) }
            .sorted { left, right in
                if left.isDirectory != right.isDirectory { return left.isDirectory }
                switch sortMode {
                case .name:
                    return left.name.localizedStandardCompare(right.name) == .orderedAscending
                case .date:
                    return (left.modified ?? .distantPast) > (right.modified ?? .distantPast)
                case .size:
                    return (left.size ?? 0) > (right.size ?? 0)
                }
            }
    }
}
