using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using PrivLock.Application.Services;
using PrivLock.Domain.Models;
using PrivLock.Domain.Results;
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
        // 1. Check for internal privileged worker or execution flag
        // This allows on-demand elevated execution within the SAME single executable.
        if (args.Length >= 2 && args[0].Equals("--privileged-worker", StringComparison.OrdinalIgnoreCase))
        {
            var pipeName = args[1];
            if (OperatingSystem.IsWindows())
            {
                return Platform.Windows.Privileged.WindowsPrivilegedWorker.Run(pipeName);
            }
            return 1;
        }

        if (args.Length >= 3 && args[0].Equals("--privileged-exec", StringComparison.OrdinalIgnoreCase))
        {
            return HandlePrivilegedExecution(args);
        }

        // 2. Initialize standard logging
        LoggingConfiguration.Initialize();
        Log.Information("=== PrivLock Desktop Starting ===");

        // 3. Global exception handlers
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
            // 4. Build Dependency Injection Service Provider
            var services = new ServiceCollection();
            ConfigureServices(services);
            using var serviceProvider = services.BuildServiceProvider();

            // 5. Handle CLI cleanup flag (--unblock-and-exit)
            if (args.Any(a => a.Equals("--unblock-and-exit", StringComparison.OrdinalIgnoreCase) ||
                             a.Equals("-u", StringComparison.OrdinalIgnoreCase)))
            {
                Log.Information("Launched with --unblock-and-exit flag. Performing silent cleanup...");
                var protectionService = serviceProvider.GetRequiredService<ProtectionService>();
                var settingsService = serviceProvider.GetRequiredService<SettingsService>();

                protectionService.DisableStandardProtectionAsync(BlockTarget.Both).GetAwaiter().GetResult();
                protectionService.DisableSecureProtectionAsync(BlockTarget.Both).GetAwaiter().GetResult();
                settingsService.SetAutostart(false);

                Log.Information("Cleanup complete. Exiting.");
                Log.CloseAndFlush();
                return 0;
            }

            // 6. Single-instance check
            var singleInstanceGuard = serviceProvider.GetRequiredService<ISingleInstanceGuard>();
            if (!singleInstanceGuard.TryAcquireSingleInstance())
            {
                Log.Warning("Another instance of PrivLock is already running. Exiting.");
                Log.CloseAndFlush();
                return 0;
            }

            // 7. Initialize localization
            var localizationService = serviceProvider.GetRequiredService<LocalizationService>();
            localizationService.Initialize();

            // 8. Start Avalonia Application
            var mainViewModel = serviceProvider.GetRequiredService<MainViewModel>();

            var exitCode = BuildAvaloniaApp(mainViewModel)
                .StartWithClassicDesktopLifetime(args);

            singleInstanceGuard.Release();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Platform.Windows.Privileged.WindowsPrivilegedSession.Instance.CloseSession();
            }
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

    private static int HandlePrivilegedExecution(string[] args)
    {
        // Format: --privileged-exec <command> <argument> [--result-file <path>]
        var command = args[1];
        var argument = args[2];
        string? resultFilePath = null;

        for (int i = 3; i < args.Length; i++)
        {
            if (args[i].Equals("--result-file", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                resultFilePath = args[i + 1];
                break;
            }
        }

        OperationResult result;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            result = Platform.Windows.Privileged.WindowsPrivilegedExecutor.ExecutePrivilegedCommand(command, argument);
        }
        else
        {
            result = OperationResult.Fail($"Privileged execution not implemented for {RuntimeInformation.OSDescription}");
        }

        if (!string.IsNullOrEmpty(resultFilePath))
        {
            try
            {
                var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });
                File.WriteAllText(resultFilePath, json);
            }
            catch { /* Best effort */ }
        }

        return result.Success ? 0 : 1;
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
        services.AddSingleton<Platform.Windows.Devices.WindowsCoreAudioController>();
        services.AddSingleton<Platform.Windows.Policies.WindowsPolicyManager>();
        services.AddSingleton<Platform.Windows.Policies.WindowsUserPrivacyManager>();

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
