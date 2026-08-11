using System.Drawing;
using System.Windows.Forms;
using CamMicBlocker.Application;
using CamMicBlocker.Domain.Models;
using Serilog;

namespace CamMicBlocker.UI.TrayIcon;

/// <summary>
/// Manages the system tray icon, context menu, and tooltip.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private static readonly ILogger Log = Serilog.Log.ForContext<TrayIconManager>();

    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly BlockingService _blockingService;
    private readonly StartupService _startupService;

    // Menu items
    private readonly ToolStripMenuItem _showAppItem;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _exitItem;

    // Icons (tracked for disposal)
    private Icon? _greenIcon;
    private Icon? _redIcon;
    private Icon? _yellowIcon;

    /// <summary>Fired when the user clicks Show Application.</summary>
    public event Action? ShowMainWindowRequested;

    /// <summary>Fired when the user clicks Exit Application.</summary>
    public event Action? ExitRequested;

    public TrayIconManager(BlockingService blockingService, StartupService startupService)
    {
        _blockingService = blockingService;
        _startupService = startupService;

        // Generate icons
        _greenIcon = CreatePadlockIcon(Color.SeaGreen);
        _redIcon = CreatePadlockIcon(Color.IndianRed);
        _yellowIcon = CreatePadlockIcon(Color.Goldenrod);

        // Build context menu
        _contextMenu = new ContextMenuStrip();

        // 1. Show Application
        _showAppItem = new ToolStripMenuItem("📱 Show Application")
        {
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold)
        };
        _showAppItem.Click += (_, _) => ShowMainWindowRequested?.Invoke();
        _contextMenu.Items.Add(_showAppItem);

        // 2. Lock / Unlock (Toggle Both)
        _toggleItem = new ToolStripMenuItem("🔒 Lock / Unlock");
        _toggleItem.Click += async (_, _) => await SafeExecuteAsync(() => _blockingService.ToggleAsync(BlockTarget.Both));
        _contextMenu.Items.Add(_toggleItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // 3. Exit Application
        _exitItem = new ToolStripMenuItem("❌ Exit Application");
        _exitItem.Click += (_, _) => ExitRequested?.Invoke();
        _contextMenu.Items.Add(_exitItem);

        // Create NotifyIcon
        _notifyIcon = new NotifyIcon
        {
            Icon = _greenIcon,
            Text = "CamMicBlocker — Checking...",
            Visible = true,
            ContextMenuStrip = _contextMenu
        };

        // Left click opens application window
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowMainWindowRequested?.Invoke();
            }
        };

        // Double click also opens application window
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindowRequested?.Invoke();

        // Listen for state changes
        _blockingService.StateChanged += OnStateChanged;

        Log.Information("TrayIconManager initialized");
    }

    /// <summary>
    /// Updates the tray icon and menu based on the current state.
    /// </summary>
    public void UpdateState(BlockState state)
    {
        OnStateChanged(state);
    }

    private void OnStateChanged(BlockState state)
    {
        // Update icon and tooltip
        if (state.AllBlocked)
        {
            _notifyIcon.Icon = _redIcon;
            _notifyIcon.Text = "CamMicBlocker — Camera & Microphone: BLOCKED";
            _toggleItem.Text = "🔓 Unlock (Both)";
        }
        else if (state.AllAllowed)
        {
            _notifyIcon.Icon = _greenIcon;
            _notifyIcon.Text = "CamMicBlocker — Camera & Microphone: Allowed";
            _toggleItem.Text = "🔒 Lock (Both)";
        }
        else
        {
            _notifyIcon.Icon = _yellowIcon;
            _notifyIcon.Text = "CamMicBlocker — Partial/Mixed state";
            _toggleItem.Text = "🔒 Lock / Unlock";
        }

        Log.Debug("Tray UI updated: Camera={CameraState}, Mic={MicState}",
            state.Camera.EffectiveStatus, state.Microphone.EffectiveStatus);
    }

    /// <summary>
    /// Shows a balloon notification from the tray icon.
    /// </summary>
    public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon.ShowBalloonTip(2000, title, message, icon);
    }

    private async Task SafeExecuteAsync(Func<Task<Domain.Interfaces.OperationResult>> action)
    {
        try
        {
            var result = await action();
            if (!result.Success)
            {
                ShowNotification("CamMicBlocker", $"Operation failed: {result.ErrorMessage}", ToolTipIcon.Error);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error executing tray action");
            ShowNotification("CamMicBlocker", $"Error: {ex.Message}", ToolTipIcon.Error);
        }
    }

    /// <summary>
    /// Creates a padlock icon in memory with proper GDI cleanup.
    /// </summary>
    private static Icon CreatePadlockIcon(Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using var brush = new SolidBrush(color);
        using var pen = new Pen(color, 3);

        // Padlock body
        g.FillRectangle(brush, 6, 14, 20, 15);
        // Padlock shackle (arc)
        g.DrawArc(pen, 9, 4, 14, 16, 180, 180);

        var hIcon = bmp.GetHicon();
        var tempIcon = Icon.FromHandle(hIcon);
        var icon = (Icon)tempIcon.Clone();
        tempIcon.Dispose();
        DestroyIcon(hIcon);

        return icon;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public void Dispose()
    {
        _blockingService.StateChanged -= OnStateChanged;

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();

        _greenIcon?.Dispose();
        _redIcon?.Dispose();
        _yellowIcon?.Dispose();
        _greenIcon = null;
        _redIcon = null;
        _yellowIcon = null;

        Log.Debug("TrayIconManager disposed");
    }
}
