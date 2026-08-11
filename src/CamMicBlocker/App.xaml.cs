using System.Windows;
using CamMicBlocker.Application;
using CamMicBlocker.Domain.Models;
using CamMicBlocker.Infrastructure;
using CamMicBlocker.Logging;
using CamMicBlocker.UI.Notification;
using CamMicBlocker.UI.TrayIcon;
using Serilog;

namespace CamMicBlocker;

/// <summary>
/// Application entry point.
/// 
/// Responsibilities:
/// 1. Single-instance enforcement (named Mutex)
/// 2. Logging initialization
/// 3. Service wiring (poor-man's DI — no container needed for this scale)
/// 4. System tray setup
/// 5. Hotkey registration
/// 6. Initial state check
/// 7. Graceful shutdown with resource cleanup
/// </summary>
public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private TrayIconManager? _trayIconManager;
    private HotkeyService? _hotkeyService;
    private BlockingService? _blockingService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. Single-instance check
        _singleInstanceMutex = new Mutex(true, @"Global\CamMicBlocker_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "CamMicBlocker is already running.\nCheck the system tray (notification area).",
                "CamMicBlocker",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // 2. Initialize logging
        LoggingConfiguration.Initialize();
        Log.Information("Application starting (single instance confirmed)");

        // Set up global exception handlers
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Fatal(args.ExceptionObject as Exception, "Unhandled domain exception");
            Log.CloseAndFlush();
        };

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled dispatcher exception");
            args.Handled = true; // Don't crash the app for non-fatal errors
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        try
        {
            // 3. Wire up services
            var deviceDetector = new DeviceDetector();
            var privilegedClient = new PrivilegedOperationClient();
            var policyManager = new PolicyManager(privilegedClient);
            var stateStore = new StateStore();

            _blockingService = new BlockingService(
                deviceDetector,
                privilegedClient,
                policyManager,
                stateStore);

            var startupService = new StartupService();

            // 4. Wire up state change notifications
            _blockingService.StateChanged += OnBlockStateChanged;

            // 5. Create system tray
            _trayIconManager = new TrayIconManager(_blockingService, startupService);
            _trayIconManager.ExitRequested += () =>
            {
                Log.Information("Exit requested by user");
                Shutdown();
            };

            // 6. Register hotkey
            _hotkeyService = new HotkeyService();
            _hotkeyService.HotkeyPressed += async () =>
            {
                Log.Debug("Hotkey triggered toggle");
                await _blockingService.ToggleAsync(BlockTarget.Both);
            };

            if (!_hotkeyService.Register())
            {
                Log.Warning("Failed to register hotkey Ctrl+Alt+B — another app may be using it");
                _trayIconManager.ShowNotification(
                    "CamMicBlocker",
                    "Could not register Ctrl+Alt+B hotkey. Another app may be using it.",
                    System.Windows.Forms.ToolTipIcon.Warning);
            }

            // 7. Read and display initial state
            var initialState = _blockingService.GetCurrentState();
            _trayIconManager.UpdateState(initialState);

            Log.Information("Application started successfully. Camera={Camera}, Mic={Mic}",
                initialState.Camera.EffectiveStatus, initialState.Microphone.EffectiveStatus);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to initialize application");
            System.Windows.MessageBox.Show(
                $"CamMicBlocker failed to start:\n{ex.Message}\n\nCheck logs at:\n{LoggingConfiguration.GetLogDirectory()}",
                "CamMicBlocker — Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void OnBlockStateChanged(BlockState state)
    {
        Dispatcher.BeginInvoke(() =>
        {
            // Show overlay notification
            if (state.AllBlocked)
            {
                NotificationWindow.Show("Camera & Microphone: BLOCKED", isBlocked: true);
            }
            else if (state.AllAllowed)
            {
                NotificationWindow.Show("Camera & Microphone: Allowed", isBlocked: false);
            }
            else
            {
                var cameraText = state.Camera.EffectiveStatus == BlockStatus.Blocked ? "Blocked" : "Allowed";
                var micText = state.Microphone.EffectiveStatus == BlockStatus.Blocked ? "Blocked" : "Allowed";
                NotificationWindow.Show($"Camera: {cameraText}, Mic: {micText}",
                    isBlocked: state.Camera.EffectiveStatus == BlockStatus.Blocked);
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Application shutting down");

        _hotkeyService?.Dispose();
        _trayIconManager?.Dispose();

        // Release the mutex so a new instance can start
        if (_singleInstanceMutex != null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Mutex was not owned — that's fine
            }
            _singleInstanceMutex.Dispose();
        }

        Log.Information("=== CamMicBlocker stopped ===");
        Log.CloseAndFlush();

        base.OnExit(e);
    }
}
