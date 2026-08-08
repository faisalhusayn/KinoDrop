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
                    Button {
                        model.showQRScanner = true
                    } label: {
                        Label("Scan KinoDrop QR code", systemImage: "qrcode.viewfinder")
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

    var body: some View {
        NavigationStack {
            List {
                Section {
                    Label("Connected to \(model.config.host)", systemImage: "checkmark.circle.fill")
                        .foregroundStyle(.green)
                }

                Section("Send") {
                    PhotosPicker(
                        selection: $photoItems,
                        maxSelectionCount: 50,
                        matching: .any(of: [.images, .videos])) {
                            Label("Send photos and videos", systemImage: "photo.on.rectangle.angled")
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
                        Label("Send files", systemImage: "doc.badge.plus")
                    }
                }

                Section("Transfers") {
                    if model.transfers.isEmpty {
                        Text("No transfers yet")
                            .foregroundStyle(.secondary)
                    } else {
                        ForEach(model.transfers) { transfer in
                            TransferRow(transfer: transfer)
                        }
                    }
                }

                Section("Browse") {
                    FileBrowserView(model: model)
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

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Image(systemName: transfer.direction == .upload ? "arrow.up.circle" : "arrow.down.circle")
                Text(transfer.name)
                    .lineLimit(1)
                Spacer()
                Text(stateText)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            if let progress = transfer.progress {
                ProgressView(value: progress)
            } else if case .transferring = transfer.state {
                ProgressView()
            }
        }
    }

    private var stateText: String {
        switch transfer.state {
        case .queued: return "Queued"
        case .transferring: return "Sending"
        case .completed:
            if let duration = transfer.transferDuration {
                return "Done · \(String(format: "%.1f", duration))s"
            }
            return "Done"
        case .failed(let message): return message
        case .cancelled: return "Cancelled"
        }
    }
}

struct FileBrowserView: View {
    @ObservedObject var model: AppModel

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
                ForEach(model.remoteFiles) { file in
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
    }
}
