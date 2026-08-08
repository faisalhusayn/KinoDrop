namespace KinoShare.Core.Models;

/// <summary>
/// Describes the KinoShare application workspace. The transfer folder is the
/// single directory exposed over SMB - created by the app at a user-chosen
/// location (or the default under the app home). Everything else (temp, logs,
/// settings) is internal to the app and never shared.
/// </summary>
/// <param name="RootPath">The app home directory (always under %LOCALAPPDATA%).</param>
/// <param name="TransferFolderPath">The single folder exposed over SMB.</param>
/// <param name="TempPath">Scratch space for the app itself.</param>
/// <param name="LogsPath">Application log files.</param>
/// <param name="SettingsPath">Persisted application settings.</param>
public sealed record WorkspaceInfo(
    string RootPath,
    string TransferFolderPath,
    string TempPath,
    string LogsPath,
    string SettingsPath)
{
    /// <summary>The name of the root folder of the app home.</summary>
    public const string RootFolderName = "KinoShare";

    /// <summary>The default transfer folder under the app home.</summary>
    public const string DefaultTransferFolderName = "Share";

    /// <summary>The folder created inside a user-chosen location.</summary>
    public const string CustomTransferFolderName = "KinoShare";

    /// <summary>Scratch space for the application.</summary>
    public const string TempFolderName = "Temp";

    /// <summary>Application log files.</summary>
    public const string LogsFolderName = "Logs";

    /// <summary>Persisted application settings.</summary>
    public const string SettingsFolderName = "Settings";
}
