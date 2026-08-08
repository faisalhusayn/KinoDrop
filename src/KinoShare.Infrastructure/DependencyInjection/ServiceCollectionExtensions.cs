namespace KinoShare.Infrastructure.DependencyInjection;

using KinoShare.Core.Abstractions;
using KinoShare.Core.Security;
using KinoShare.Infrastructure.Firewall;
using KinoShare.Infrastructure.Settings;
using KinoShare.Infrastructure.Smb;
using KinoShare.Infrastructure.Users;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers KinoShare's Windows-specific services with the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all KinoShare infrastructure services.
    /// </summary>
    /// <param name="services">The service collection to extend.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddKinoShareInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services
            .AddSingleton<ISmbShareService, PowerShellSmbShareService>()
            .AddSingleton<IUserAccountService, NetUserAccountService>()
            .AddSingleton<IFolderAccessService, IcaclsFolderAccessService>()
            .AddSingleton<IUserCredentialGenerator, RandomUserCredentialGenerator>()
            .AddSingleton<IAppSettingsService, AppSettingsService>()
            .AddSingleton<ITransferHistoryService, TransferHistoryService>()
            .AddSingleton<IDeviceCredentialStore, DeviceCredentialStore>()
            .AddSingleton<IFirewallService, NetFirewallService>();
    }
}
