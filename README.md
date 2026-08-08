# KinoDrop

KinoDrop is a Windows desktop app for transferring files between a Windows PC and an iPhone over the local network.

It uses Windows' built-in SMB server. The iPhone can connect through the Files app or the companion iOS app in `ios/`.

## Features

- Share a selected transfer folder over SMB
- Send files from the PC to the iPhone
- Receive files from the iPhone
- User-set connection password, stored encrypted with Windows DPAPI
- QR code for quickly opening the SMB connection in iOS Files
- Live transfer progress, speed, and status
- Transfer history and Windows notifications
- Automatic SMB firewall rule management
- Inno Setup installer for Windows x64

## Requirements

- Windows 10 version 1809 or newer
- Windows x64
- .NET 9 SDK for development
- Administrator privileges when creating a sharing session

The iPhone and PC must be connected to the same local network.

## Build

```powershell
dotnet build KinoShare.sln -c Debug
```

Run the test suite:

```powershell
dotnet test tests/KinoShare.Tests -c Debug
```

Create a self-contained Windows release:

```powershell
dotnet publish src/KinoShare.UI/KinoShare.UI.csproj `
  -c Release `
  -r win-x64 `
  --self-contained `
  -o artifacts\publish
```

The installer script is located at `installer/installer.iss` and requires Inno Setup 6.

## Connection

KinoDrop creates a temporary Windows account named `kinoshare` for each sharing session. The account is removed when the session stops. The selected password is reused across sessions and stored encrypted on the PC.

The SMB share name remains `KinoShare` for compatibility. A connection normally looks like:

```text
smb://192.168.1.100/KinoShare
```

The `smb://` URL negotiates SMB2 or SMB3 with Windows; SMB1 is not required.

## Data Location

User settings, transfer history, and the encrypted credential are stored in:

```text
%LOCALAPPDATA%\KinoShare\Settings\
```

This location is intentionally preserved across application upgrades.

## Project Layout

- `src/KinoShare.UI` - WinUI 3 desktop application
- `src/KinoShare.Core` - application contracts and business logic
- `src/KinoShare.Infrastructure` - Windows SMB, firewall, account, and storage services
- `tests/KinoShare.Tests` - unit tests
- `installer` - Inno Setup installer definition
- `ios` - SwiftUI iPhone companion app and GitHub Actions build configuration
