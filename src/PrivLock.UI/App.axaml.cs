using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using PrivLock.UI.ViewModels;
using PrivLock.UI.Views;
using Serilog;

namespace PrivLock.UI;

public partial class App : Avalonia.Application
{
    private static readonly ILogger Log = Serilog.Log.ForContext<App>();

    private readonly MainViewModel? _mainViewModel;
    private TrayIcon? _trayIcon;
    private Window? _mainWindow;

    // Default constructor for designer
    public App()
    {
    }

    public App(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mainWindow = new MainWindow
            {
                DataContext = _mainViewModel
            };
            desktop.MainWindow = _mainWindow;

            // Setup Tray Icon
            SetupTrayIcon(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            WindowIcon? windowIcon = null;

            try
            {
                var uri = new Uri("avares://PrivLock.UI/Assets/logo.png");
                if (AssetLoader.Exists(uri))
                {
                    using var stream = AssetLoader.Open(uri);
                    var bitmap = new Bitmap(stream);
                    windowIcon = new WindowIcon(bitmap);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not load logo asset for tray icon, using system default icon");
            }

            if (_mainWindow != null && windowIcon != null)
            {
                _mainWindow.Icon = windowIcon;
            }

            var nativeMenu = new NativeMenu();

            var openItem = new NativeMenuItem("Abrir / Open PrivLock");
            openItem.Click += (_, _) => ShowMainWindow();

            var exitItem = new NativeMenuItem("Salir / Exit");
            exitItem.Click += (_, _) =>
            {
                _trayIcon?.Dispose();
                desktop.Shutdown();
            };

            nativeMenu.Items.Add(openItem);
            nativeMenu.Items.Add(new NativeMenuItemSeparator());
            nativeMenu.Items.Add(exitItem);

            _trayIcon = new TrayIcon
            {
                ToolTipText = "PrivLock — Camera & Microphone Blocker",
                IsVisible = true,
                Menu = nativeMenu
            };

            if (windowIcon != null)
            {
                _trayIcon.Icon = windowIcon;
            }

            _trayIcon.Clicked += (_, _) => ShowMainWindow();

            var trayIcons = new TrayIcons { _trayIcon };
            TrayIcon.SetIcons(this, trayIcons);

            Log.Information("System Tray Icon initialized successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize System Tray Icon");
        }
    }

    public void ShowMainWindow()
    {
        if (_mainWindow == null) return;

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
        _mainWindow.BringIntoView();
    }
}
