namespace KinoShare.UI.Services;

using KinoShare.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

/// <summary>
/// Shows Windows toast notifications for completed transfers via the Windows
/// App SDK notification platform (works for unpackaged apps).
/// </summary>
public sealed class ToastService : IToastService
{
    private readonly ILogger<ToastService> _logger;
    private bool _registered;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToastService"/> class.
    /// </summary>
    public ToastService(ILogger<ToastService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public void ShowTransferCompleted(string direction, string fileName, string sizeText)
    {
        try
        {
            RegisterOnce();

            var notification = new AppNotificationBuilder()
                .AddText(direction == "Received" ? "Received a file" : "Sent a file")
                .AddText($"{fileName} ({sizeText})")
                .SetTag("transfer")
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to show a transfer notification.");
        }
    }

    private void RegisterOnce()
    {
        if (_registered)
        {
            return;
        }

        AppNotificationManager.Default.Register();
        _registered = true;
    }
}
