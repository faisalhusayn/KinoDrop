namespace KinoShare.Core;

/// <summary>
/// Product-wide constants. Single source of truth so callers and end-to-end
/// scripts never hard-code values that could drift apart.
/// </summary>
public static class KinoShareDefaults
{
    /// <summary>The single SMB share name clients connect to.</summary>
    public const string ShareName = "KinoShare";
}
