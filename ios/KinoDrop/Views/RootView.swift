import AVKit
import PDFKit
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
    @State private var showManualConnection = false

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
                    Button {
                        model.showQRScanner = true
                    } label: {
                        Label("Scan QR code to pair", systemImage: "qrcode.viewfinder")
                    }

                    Text("Use the QR code shown by the Windows app for a first connection. Your credentials are saved securely on this iPhone.")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                } header: {
                    Text("Pair a New PC")
                }

                if let pendingName = model.pendingConnectionName {
                    Section("Ready to Connect") {
                        Label(pendingName, systemImage: "desktopcomputer")
                            .font(.headline)
                        Text("The QR code supplied the connection details. Your password will be stored securely after connecting.")
                            .font(.footnote)
                            .foregroundStyle(.secondary)
                        Button {
                            Task { await model.connect() }
                        } label: {
                            Label("Connect to \(pendingName)", systemImage: "bolt.horizontal.circle.fill")
                        }
                        .disabled(model.connectionState == .connecting)
                    }
                }

                if !model.savedConnections.isEmpty {
                    Section {
                        ForEach(model.savedConnections) { connection in
                            HStack {
                                Button {
                                    model.useSavedConnection(connection)
                                    Task { await model.connect() }
                                } label: {
                                    Label(connection.name, systemImage: "desktopcomputer")
                                }
                                Spacer()
                                Button("Remove", role: .destructive) {
                                    model.deleteSavedConnection(connection)
                                }
                                .buttonStyle(.bordered)
                            }
                        }

                        if !model.config.host.isEmpty && !model.config.password.isEmpty {
                            DisclosureGroup("Connection details") {
                                LabeledContent("Address", value: model.config.host)
                                LabeledContent("Share", value: model.config.share)
                            }
                        }
                    } header: {
                        Text("Saved Connections")
                    }
                }

                Section {
                    DisclosureGroup("Enter connection manually", isExpanded: $showManualConnection) {
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
                    }

                    Button {
                        Task { await model.connect() }
                    } label: {
                        if model.connectionState == .connecting {
                            ProgressView()
                        } else {
                            Label("Connect manually", systemImage: "bolt.horizontal.circle.fill")
                        }
                    }
                    .disabled(!showManualConnection || model.config.host.isEmpty || model.config.password.isEmpty)
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
                        ScrollView(.horizontal, showsIndicators: false) {
                            HStack {
                                Button(model.isQueuePaused ? "Resume queue" : "Pause queue") {
                                    model.toggleQueuePause()
                                }
                                .buttonStyle(.bordered)
                                Button("Retry failed") {
                                    model.retryAllFailed()
                                }
                                .buttonStyle(.bordered)
                                Button("Cancel all", role: .destructive) {
                                    Task { await model.cancelAllQueued() }
                                }
                                .buttonStyle(.bordered)
                            }
                        }

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
                    NavigationLink {
                        FileBrowserView(model: model)
                    } label: {
                        Label("Browse files", systemImage: "folder")
                        Spacer()
                        if !model.remoteFiles.isEmpty {
                            Text("\(model.remoteFiles.count)")
                                .foregroundStyle(.secondary)
                        }
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
            } else if case .transferring = transfer.state, let progress = transfer.progress {
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
                    Button("Cancel", role: .destructive) { Task { await model.cancel(transfer) } }
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
    @State private var viewMode = ViewMode.grid
    @State private var selectedVideo: RemoteFile?

    private enum SortMode: String, CaseIterable {
        case name = "Name"
        case date = "Date"
        case size = "Size"
    }

    private enum ViewMode: String, CaseIterable {
        case grid
        case list
    }

    var body: some View {
        Group {
            if sortedFiles.isEmpty {
                ContentUnavailableView("No files", systemImage: "folder", description: Text("This folder is empty."))
            } else if viewMode == .grid {
                ScrollView {
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 150), spacing: 16)], spacing: 18) {
                        ForEach(sortedFiles) { file in
                            FileGridItem(file: file, model: model) { selectedVideo = $0 }
                        }
                    }
                    .padding()
                }
            } else {
                List(sortedFiles) { file in
                    FileListItem(file: file, model: model) { selectedVideo = $0 }
                }
                .listStyle(.plain)
            }
        }
        .navigationTitle(model.browsePath.isEmpty ? "Browse" : (model.browsePath as NSString).lastPathComponent)
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .topBarLeading) {
                if !model.browsePath.isEmpty {
                    Button {
                        Task { await model.goUp() }
                    } label: {
                        Label("Up", systemImage: "chevron.left")
                    }
                }
            }
            ToolbarItemGroup(placement: .topBarTrailing) {
                Menu {
                    Picker("View", selection: $viewMode) {
                        Label("Grid", systemImage: "square.grid.2x2").tag(ViewMode.grid)
                        Label("List", systemImage: "list.bullet").tag(ViewMode.list)
                    }
                    Picker("Sort by", selection: $sortMode) {
                        ForEach(SortMode.allCases, id: \.self) { mode in
                            Text(mode.rawValue).tag(mode)
                        }
                    }
                } label: {
                    Image(systemName: viewMode == .grid ? "square.grid.2x2" : "list.bullet")
                }
                Button {
                    Task { await model.refreshFiles() }
                } label: {
                    Image(systemName: "arrow.clockwise")
                }
            }
        }
        .task { await model.refreshFiles() }
        .searchable(text: $searchText, prompt: "Search files")
        .sheet(item: $selectedVideo) { file in
            SMBRemotePreviewView(file: file, client: model.smb) {
                model.download(file)
            }
        }
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

private struct FileGridItem: View {
    let file: RemoteFile
    @ObservedObject var model: AppModel
    let onPreview: (RemoteFile) -> Void

    var body: some View {
        Button {
            activate()
        } label: {
            VStack(alignment: .leading, spacing: 8) {
                RemoteFileThumbnail(file: file, model: model)
                    .frame(maxWidth: .infinity)
                    .aspectRatio(1, contentMode: .fit)
                Text(file.name)
                    .font(.subheadline)
                    .lineLimit(2)
                    .multilineTextAlignment(.leading)
                if !file.isDirectory {
                    Text(file.size.map { ByteCountFormatter.string(fromByteCount: $0, countStyle: .file) } ?? "")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .buttonStyle(.plain)
        .contextMenu {
            if !file.isDirectory {
                Button { model.download(file) } label: {
                    Label("Download", systemImage: "arrow.down.circle")
                }
            }
        }
    }

    private func activate() {
        if file.isDirectory {
            Task { await model.open(file) }
        } else if file.canPreview {
            onPreview(file)
        } else {
            model.download(file)
        }
    }
}

private struct FileListItem: View {
    let file: RemoteFile
    @ObservedObject var model: AppModel
    let onPreview: (RemoteFile) -> Void

    var body: some View {
        Button {
            if file.isDirectory {
                Task { await model.open(file) }
            } else if file.canPreview {
                onPreview(file)
            } else {
                model.download(file)
            }
        } label: {
            HStack(spacing: 12) {
                RemoteFileThumbnail(file: file, model: model)
                    .frame(width: 48, height: 48)
                VStack(alignment: .leading, spacing: 3) {
                    Text(file.name)
                        .lineLimit(1)
                    Text(file.isDirectory ? "Folder" : file.size.map { ByteCountFormatter.string(fromByteCount: $0, countStyle: .file) } ?? "File")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                Spacer()
                Image(systemName: file.isDirectory ? "chevron.right" : "arrow.down.circle")
                    .foregroundStyle(.secondary)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}

private struct SMBRemotePreviewView: View {
    let file: RemoteFile
    let client: SMBClient
    let onDownload: () -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var player: AVPlayer?
    @State private var image: UIImage?
    @State private var pdfDocument: PDFDocument?
    @State private var text: String?
    @State private var errorMessage: String?
    private let loader: SMBVideoResourceLoader

    init(file: RemoteFile, client: SMBClient, onDownload: @escaping () -> Void) {
        self.file = file
        self.client = client
        self.onDownload = onDownload
        self.loader = SMBVideoResourceLoader(client: client, file: file)
    }

    var body: some View {
        NavigationStack {
            Group {
                if let player {
                    VideoPlayer(player: player)
                        .aspectRatio(contentMode: .fit)
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                        .background(.black)
                        .onDisappear { player.pause() }
                } else if let image {
                    RemoteImagePreview(image: image)
                } else if let pdfDocument {
                    PDFDocumentView(document: pdfDocument)
                } else if let text {
                    ScrollView([.vertical, .horizontal]) {
                        Text(text)
                            .font(.system(.body, design: .monospaced))
                            .textSelection(.enabled)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding()
                    }
                } else if let errorMessage {
                    VStack(spacing: 16) {
                        ContentUnavailableView("Preview unavailable", systemImage: "doc.questionmark", description: Text(errorMessage))
                        Button("Download file", action: onDownload)
                            .buttonStyle(.borderedProminent)
                    }
                } else {
                    ProgressView("Loading preview...")
                }
            }
            .navigationTitle(file.name)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Button("Done") { dismiss() }
                }
            }
        }
        .task {
            await preparePlayer()
        }
    }

    private func preparePlayer() async {
        do {
            if file.isVideo || file.isAudio {
                guard let url = URL(string: "kinodrop-smb://media/") else { return }
                let asset = AVURLAsset(url: url)
                asset.resourceLoader.setDelegate(loader, queue: DispatchQueue(label: "com.faisalhusayn.kinodrop.media-loader"))
                let newPlayer = AVPlayer(playerItem: AVPlayerItem(asset: asset))
                player = newPlayer
                newPlayer.play()
            } else {
                let data = try await client.readAll(remotePath: file.path)
                if file.isImage {
                    image = UIImage(data: data)
                } else if file.isPDF {
                    pdfDocument = PDFDocument(data: data)
                } else if file.isText {
                    text = String(data: data, encoding: .utf8) ?? "Unable to decode this text file."
                }
                if image == nil && pdfDocument == nil && text == nil {
                    errorMessage = "This file format cannot be previewed on iPhone."
                }
            }
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}

private struct PDFDocumentView: UIViewRepresentable {
    let document: PDFDocument

    func makeUIView(context: Context) -> PDFView {
        let view = PDFView()
        view.autoScales = true
        view.displayMode = .singlePageContinuous
        view.document = document
        return view
    }

    func updateUIView(_ view: PDFView, context: Context) {
        view.document = document
    }
}

private struct RemoteImagePreview: View {
    let image: UIImage
    @State private var scale: CGFloat = 1
    @State private var baseScale: CGFloat = 1

    var body: some View {
        GeometryReader { geometry in
            ZStack {
                Color.black.ignoresSafeArea()
                Image(uiImage: image)
                    .resizable()
                    .scaledToFit()
                    .frame(maxWidth: geometry.size.width, maxHeight: geometry.size.height)
                    .scaleEffect(scale)
                    .contentShape(Rectangle())
                    .gesture(
                        MagnificationGesture()
                            .onChanged { value in
                                scale = min(max(baseScale * value, 1), 4)
                            }
                            .onEnded { _ in
                                withAnimation(.easeOut) {
                                    scale = min(max(scale, 1), 4)
                                    baseScale = scale
                                }
                            })
                    .onTapGesture(count: 2) {
                        withAnimation(.easeInOut) {
                            scale = scale > 1 ? 1 : 2
                            baseScale = scale
                        }
                    }
            }
        }
    }
}

private struct RemoteFileThumbnail: View {
    let file: RemoteFile
    @ObservedObject var model: AppModel
    @State private var image: UIImage?
    @State private var isLoading = false

    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 12)
                .fill(file.isDirectory ? Color.blue.opacity(0.12) : Color.secondary.opacity(0.1))
            if let image {
                Image(uiImage: image)
                    .resizable()
                    .scaledToFill()
                    .clipShape(RoundedRectangle(cornerRadius: 12))
            } else if isLoading {
                ProgressView()
            } else {
                Image(systemName: file.isDirectory ? "folder.fill" : fileIcon)
                    .font(.system(size: 34))
                    .foregroundStyle(file.isDirectory ? .blue : .secondary)
            }
        }
        .task(id: file.id) {
            guard file.isPreviewable else { return }
            isLoading = true
            image = await model.thumbnail(for: file)
            isLoading = false
        }
    }

    private var fileIcon: String {
        if file.isVideo { return "video.fill" }
        if file.isAudio { return "waveform" }
        switch file.name.split(separator: ".").last?.lowercased() {
        case "pdf": return "doc.richtext"
        case "zip", "7z", "rar": return "doc.zipper"
        case "mp3", "wav", "m4a": return "music.note"
        case "txt", "md", "rtf": return "doc.text"
        default: return "doc"
        }
    }
}
