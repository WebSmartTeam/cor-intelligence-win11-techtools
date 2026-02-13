using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wpf.Ui;
using Wpf.Ui.DependencyInjection;
using CORCleanup.ViewModels;
using CORCleanup.Views;

namespace CORCleanup;

public partial class App : Application
{
    private static readonly IHost _host = Host.CreateDefaultBuilder()
        .ConfigureServices((context, services) =>
        {
            // WPF-UI navigation infrastructure
            services.AddNavigationViewPageProvider();
            services.AddSingleton<INavigationService, NavigationService>();

            // Main window
            services.AddSingleton<MainWindow>();
            services.AddSingleton<MainWindowViewModel>();

            // Views — transient (new instance per navigation)
            services.AddTransient<HomePage>();
            services.AddTransient<NetworkPage>();
            services.AddTransient<CleanupPage>();
            services.AddTransient<RegistryPage>();
            services.AddTransient<UninstallerPage>();
            services.AddTransient<HardwarePage>();
            services.AddTransient<ToolsPage>();
            services.AddTransient<AdminPage>();
            services.AddTransient<SettingsPage>();

            // ViewModels — transient (tied to page lifecycle)
            services.AddTransient<HomeViewModel>();
            services.AddTransient<NetworkViewModel>();
            services.AddTransient<CleanupViewModel>();
            services.AddTransient<RegistryViewModel>();
            services.AddTransient<UninstallerViewModel>();
            services.AddTransient<HardwareViewModel>();
            services.AddTransient<ToolsViewModel>();
            services.AddTransient<AdminViewModel>();
            services.AddTransient<SettingsViewModel>();

            // Core services — singletons (one instance, reused)
            services.AddSingleton<Core.Interfaces.IPingService, Core.Services.Network.PingService>();
            services.AddSingleton<Core.Interfaces.ISystemInfoService, Core.Services.Hardware.SystemInfoService>();
            services.AddSingleton<Core.Interfaces.ICleanupService, Core.Services.Cleanup.SystemCleanupService>();
            services.AddSingleton<Core.Interfaces.IWifiService, Core.Services.Network.WifiService>();
            services.AddSingleton<Core.Interfaces.IWifiScannerService, Core.Services.Network.WifiScannerService>();
            services.AddSingleton<Core.Interfaces.IUninstallService, Core.Services.UninstallService>();
            services.AddSingleton<Core.Interfaces.IStartupService, Core.Services.Admin.StartupService>();
            services.AddSingleton<Core.Interfaces.IServicesManagerService, Core.Services.Admin.ServicesManagerService>();
            services.AddSingleton<Core.Interfaces.IEventLogService, Core.Services.Admin.EventLogService>();
            services.AddSingleton<Core.Interfaces.IFileHashService, Core.Services.Tools.FileHashService>();
            services.AddSingleton<Core.Interfaces.IPasswordGeneratorService, Core.Services.Tools.PasswordGeneratorService>();
            services.AddSingleton<Core.Interfaces.IRegistryCleanerService, Core.Services.Registry.RegistryCleanerService>();
            services.AddSingleton<Core.Interfaces.ITracerouteService, Core.Services.Network.TracerouteService>();
            services.AddSingleton<Core.Interfaces.IDnsLookupService, Core.Services.Network.DnsLookupService>();
            services.AddSingleton<Core.Interfaces.IPortScannerService, Core.Services.Network.PortScannerService>();
            services.AddSingleton<Core.Interfaces.INetworkInfoService, Core.Services.Network.NetworkInfoService>();
            services.AddSingleton<Core.Interfaces.ISubnetCalculatorService, Core.Services.Network.SubnetCalculatorService>();
            services.AddSingleton<Core.Interfaces.IBsodViewerService, Core.Services.Tools.BsodViewerService>();
            services.AddSingleton<Core.Interfaces.ISoftwareInventoryService, Core.Services.Tools.SoftwareInventoryService>();
            services.AddSingleton<Core.Interfaces.IAntivirusService, Core.Services.Tools.AntivirusService>();
            services.AddSingleton<Core.Interfaces.ISystemRepairService, Core.Services.Admin.SystemRepairService>();
            services.AddSingleton<Core.Interfaces.IPrinterService, Core.Services.Admin.PrinterService>();
            services.AddSingleton<Core.Interfaces.IHostsFileService, Core.Services.Admin.HostsFileService>();
            services.AddSingleton<Core.Interfaces.IDebloatService, Core.Services.Admin.DebloatService>();
        })
        .Build();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _host.Start();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        Task.Run(() => _host.StopAsync()).GetAwaiter().GetResult();
        _host.Dispose();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // TODO: Log to %APPDATA%\COR Cleanup\Logs\{date}.log
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}",
            "COR Cleanup — Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
