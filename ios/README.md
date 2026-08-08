# KinoDrop iOS

The iPhone companion app for KinoDrop Windows.

KinoDrop iOS connects directly to the existing Windows SMB share. The Windows backend does not need an iOS-specific server or protocol change.

## Planned Features

- Scan the QR code shown by the Windows app
- Save the connection securely in the iOS Keychain
- Send multiple photos and videos from Photos
- Send documents from Files
- Show upload progress
- Browse the Windows share
- Download files and share them to Photos or Files

## Technology

- Swift and SwiftUI
- iOS 16+
- AMSMB2 for SMB2/SMB3 file operations
- PhotosPicker for photo and video selection
- File importer for Files integration
- AVFoundation for QR scanning
- XcodeGen for project generation

## Windows Backend

The app connects to the existing share:

```text
smb://<pc-ip>/KinoShare
```

The Windows app creates the `kinoshare` account and manages the share, password, firewall, transfer monitoring, history, and notifications.

## Development

The project is developed on Windows and built on a GitHub Actions macOS runner. XcodeGen creates the Xcode project during the workflow.

The first build produces an unsigned `KinoDrop.ipa` artifact for installation through a personal sideloading workflow such as AltStore.

## Privacy

Files transfer directly between the iPhone and the Windows PC over the local network. No cloud relay is used.
