using System.Text;
using KinoShare.App.Workflow;
using KinoShare.Core.Abstractions;
using KinoShare.Core.Services;
using KinoShare.Infrastructure.DependencyInjection;
using KinoShare.Infrastructure.Monitoring;
using KinoShare.Infrastructure.Network;
using KinoShare.Infrastructure.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;
// Setting OutputEncoding recreates Console.Out, and a recreated writer is
// buffered (AutoFlush = false) when stdout is redirected - e.g. a parent
// process reading the console live - so force per-line flushing.
Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), Console.OutputEncoding)
{
    AutoFlush = true,
});

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// --folder <location> overrides the persisted choice for this launch.
string? folderArgument = ParseFolderArgument(args);

builder.Services.AddKinoShareInfrastructure();
builder.Services.AddSingleton(sp =>
{
    // --folder <location> wins; otherwise the persisted choice applies.
    string? location = folderArgument ?? sp.GetRequiredService<IAppSettingsService>()
        .GetTransferFolderLocationAsync()
        .GetAwaiter()
        .GetResult();

    ILogger<WorkspaceService> logger = sp.GetRequiredService<ILogger<WorkspaceService>>();
    return new WorkspaceService(logger, location);
});
builder.Services.AddSingleton<IWorkspaceService>(sp => sp.GetRequiredService<WorkspaceService>());
builder.Services.AddSingleton<INetworkService, NetworkService>();
builder.Services.AddSingleton<ITransferMonitorService, FileSystemTransferMonitor>();
builder.Services.AddSingleton<ShareManager>();
builder.Services.AddSingleton<ShareWorkflow>();

using IHost host = builder.Build();

ShareWorkflow workflow = host.Services.GetRequiredService<ShareWorkflow>();
int exitCode = await workflow.RunAsync();

return exitCode;

static string? ParseFolderArgument(string[] args)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], "--folder", StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}
