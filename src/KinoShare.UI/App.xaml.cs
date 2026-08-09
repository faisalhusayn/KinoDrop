namespace KinoShare.UI;

using KinoShare.Core.Abstractions;
using KinoShare.Core.Services;
using KinoShare.Infrastructure.DependencyInjection;
using KinoShare.Infrastructure.Monitoring;
using KinoShare.Infrastructure.Network;
using KinoShare.Infrastructure.Workspace;
using KinoShare.UI.Services;
using KinoShare.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

/// <summary>
/// The WinUI 3 application entry point. Builds the same service composition
/// as the console app and hosts a single main window.
/// </summary>
public partial class App : Application
{
    private readonly ServiceProvider _services;
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();

        ServiceCollection services = new();
        services.AddKinoShareInfrastructure();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddDebug();
        });

        services.AddSingleton<Func<string?, WorkspaceService>>(sp =>
        {
            ILogger<WorkspaceService> logger = sp.GetRequiredService<ILogger<WorkspaceService>>();
            return location => new WorkspaceService(logger, location);
        });
        services.AddSingleton<INetworkService, NetworkService>();
        services.AddSingleton<ITransferMonitorService, FileSystemTransferMonitor>();
        services.AddSingleton<IToastService, ToastService>();
        services.AddSingleton<ShareManager>();
        services.AddSingleton(DispatcherQueue.GetForCurrentThread());
        services.AddSingleton<ShareSessionViewModel>();

        _services = services.BuildServiceProvider();
    }

    /// <summary>
    /// Gets the application's service provider.
    /// </summary>
    public IServiceProvider Services => _services;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
