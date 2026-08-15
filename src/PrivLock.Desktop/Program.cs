using System.Runtime.InteropServices;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using PrivLock.Application.Services;
using PrivLock.Domain.Models;
using PrivLock.Infrastructure.Common.Logging;
using PrivLock.Infrastructure.Common.Storage;
using PrivLock.Platform.Abstractions;
using PrivLock.UI;
using PrivLock.UI.ViewModels;
using Serilog;

namespace PrivLock.Desktop;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // 1. Initialize logging
        LoggingConfiguration.Initialize();
        Log.Information("=== PrivLock Desktop Starting ===");

        // 2. Global exception handlers
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                var reportPath = CrashReporter.GenerateCrashReport(ex, "AppDomain.UnhandledException");
                Log.Fatal(ex, "Fatal domain exception. Crash report: {ReportPath}", reportPath);
            }
            Log.CloseAndFlush();
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            if (e.Exception is Exception ex)
            {
                var reportPath = CrashReporter.GenerateCrashReport(ex, "TaskScheduler.UnobservedTaskException");
                Log.Error(ex, "Unobserved task exception: {ReportPath}", reportPath);
            }
            e.SetObserved();
        };

        try
        {
            // 3. Build Dependency Injection Service Provider
            var services = new ServiceCollection();
            ConfigureServices(services);
            using var serviceProvider = services.BuildServiceProvider();

            // 4. Handle CLI cleanup flag (--unblock-and-exit)
            if (args.Any(a => a.Equals("--unblock-and-exit", StringComparison.OrdinalIgnoreCase) ||
                             a.Equals("-u", StringComparison.OrdinalIgnoreCase)))
            {
                Log.Information("Launched with --unblock-and-exit flag. Performing silent cleanup...");
                var protectionService = serviceProvider.GetRequiredService<ProtectionService>();
                var settingsService = serviceProvider.GetRequiredService<SettingsService>();

                protectionService.UnblockAsync(BlockTarget.Both).GetAwaiter().GetResult();
                settingsService.SetAutostart(false);

                Log.Information("Cleanup complete. Exiting.");
                Log.CloseAndFlush();
                return 0;
            }

            // 5. Single-instance check
            var singleInstanceGuard = serviceProvider.GetRequiredService<ISingleInstanceGuard>();
            if (!singleInstanceGuard.TryAcquireSingleInstance())
            {
                Log.Warning("Another instance of PrivLock is already running. Exiting.");
                Log.CloseAndFlush();
                return 0;
            }

            // 6. Initialize localization
            var localizationService = serviceProvider.GetRequiredService<LocalizationService>();
            localizationService.Initialize();

            // 7. Start Avalonia Application
            var mainViewModel = serviceProvider.GetRequiredService<MainViewModel>();

            var exitCode = BuildAvaloniaApp(mainViewModel)
                .StartWithClassicDesktopLifetime(args);

            singleInstanceGuard.Release();
            Log.Information("=== PrivLock Exited Cleanly (Code: {Code}) ===", exitCode);
            Log.CloseAndFlush();
            return exitCode;
        }
        catch (Exception ex)
        {
            var reportPath = CrashReporter.GenerateCrashReport(ex, "Program.Main");
            Log.Fatal(ex, "Unhandled exception during application lifecycle: {ReportPath}", reportPath);
            Log.CloseAndFlush();
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp(MainViewModel viewModel) =>
        AppBuilder.Configure<App>(() => new App(viewModel))
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void ConfigureServices(IServiceCollection services)
    {
        // 1. Common Storage & Infrastructure
        services.AddSingleton<IStateStore, FileStateStore>();

        // 2. Platform-Specific Native Providers
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ConfigureWindowsServices(services);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            ConfigureLinuxServices(services);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            ConfigureMacServices(services);
        }
        else
        {
            throw new PlatformNotSupportedException($"Unsupported operating system: {RuntimeInformation.OSDescription}");
        }

        // 3. Application Services
        services.AddSingleton<ProtectionService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<LocalizationService>();

        // 4. UI ViewModels
        services.AddSingleton<MainViewModel>();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void ConfigureWindowsServices(IServiceCollection services)
    {
        services.AddSingleton<Platform.Windows.Devices.WindowsDeviceDetector>();
        services.AddSingleton<Platform.Windows.Devices.WindowsDeviceController>();
        services.AddSingleton<Platform.Windows.Policies.WindowsPolicyManager>();

        services.AddSingleton<IDeviceDetector>(sp => sp.GetRequiredService<Platform.Windows.Devices.WindowsDeviceDetector>());
        services.AddSingleton<IDeviceProtectionProvider, Platform.Windows.WindowsProtectionProvider>();
        services.AddSingleton<IElevationProvider, Platform.Windows.Elevation.WindowsElevationProvider>();
        services.AddSingleton<IPlatformCapabilityProvider, Platform.Windows.WindowsCapabilityProvider>();
        services.AddSingleton<IAutostartProvider, Platform.Windows.System.WindowsAutostartProvider>();
        services.AddSingleton<IGlobalHotkeyProvider, Platform.Windows.System.WindowsHotkeyProvider>();
        services.AddSingleton<ISingleInstanceGuard, Platform.Windows.System.WindowsSingleInstanceGuard>();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static void ConfigureLinuxServices(IServiceCollection services)
    {
        services.AddSingleton<Platform.Linux.Devices.LinuxDeviceDetector>();
        services.AddSingleton<Platform.Linux.Devices.LinuxDeviceController>();

        services.AddSingleton<IDeviceDetector>(sp => sp.GetRequiredService<Platform.Linux.Devices.LinuxDeviceDetector>());
        services.AddSingleton<IDeviceProtectionProvider, Platform.Linux.LinuxProtectionProvider>();
        services.AddSingleton<IElevationProvider, Platform.Linux.Elevation.LinuxElevationProvider>();
        services.AddSingleton<IPlatformCapabilityProvider, Platform.Linux.LinuxCapabilityProvider>();
        services.AddSingleton<IAutostartProvider, Platform.Linux.System.LinuxAutostartProvider>();
        services.AddSingleton<ISingleInstanceGuard, Platform.Linux.System.LinuxSingleInstanceGuard>();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("osx")]
    private static void ConfigureMacServices(IServiceCollection services)
    {
        services.AddSingleton<Platform.MacOS.Devices.MacOSDeviceDetector>();
        services.AddSingleton<Platform.MacOS.Devices.MacOSDeviceController>();

        services.AddSingleton<IDeviceDetector>(sp => sp.GetRequiredService<Platform.MacOS.Devices.MacOSDeviceDetector>());
        services.AddSingleton<IDeviceProtectionProvider, Platform.MacOS.MacOSProtectionProvider>();
        services.AddSingleton<IElevationProvider, Platform.MacOS.Elevation.MacOSElevationProvider>();
        services.AddSingleton<IPlatformCapabilityProvider, Platform.MacOS.MacOSCapabilityProvider>();
        services.AddSingleton<IAutostartProvider, Platform.MacOS.System.MacOSAutostartProvider>();
        services.AddSingleton<ISingleInstanceGuard, Platform.MacOS.System.MacOSSingleInstanceGuard>();
    }
}
