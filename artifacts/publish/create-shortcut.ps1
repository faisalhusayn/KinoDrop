# Creates a desktop shortcut to the published KinoDrop app.
# The app manifest requires administrator rights, so the shortcut is
# created with "Run as administrator" semantics via the shortcut flags.
#
# Usage: right-click -> Run with PowerShell, or:  powershell -ExecutionPolicy Bypass -File create-shortcut.ps1

$ErrorActionPreference = "Stop"

$exe = Join-Path $PSScriptRoot "KinoShare.UI.exe"
if (-not (Test-Path -LiteralPath $exe)) {
    Write-Error "KinoShare.UI.exe not found next to this script ($PSScriptRoot)."
    exit 1
}

$desktop = [Environment]::GetFolderPath("Desktop")
$shortcutPath = Join-Path $desktop "KinoDrop.lnk"

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $PSScriptRoot
$shortcut.IconLocation = "$exe,0"
$shortcut.Description = "KinoDrop - iPhone <-> PC file sharing"
$shortcut.Save()

Write-Host "Created shortcut: $shortcutPath"
