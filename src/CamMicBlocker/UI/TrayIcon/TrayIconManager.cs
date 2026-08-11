using System.Drawing;
using System.Windows.Forms;
using CamMicBlocker.Application;
using CamMicBlocker.Domain.Models;
using CamMicBlocker.Logging;
using Serilog;

namespace CamMicBlocker.UI.TrayIcon;

/// <summary>
/// Manages the system tray icon, context menu, and tooltip.
/// Uses System.Windows.Forms.NotifyIcon (proven and reliable for system tray).
/// 
/// Icons are generated in memory as colored padlocks to avoid external file dependencies.
/// All GDI resources are properly tracked and disposed.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private static readonly ILogger Log = Serilog.Log.ForContext<TrayIconManager>();

    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly BlockingService _blockingService;
    private readonly StartupService _startupService;

    // Menu items (kept as fields for state updates)
    private readonly ToolStripMenuItem _statusCameraItem;
    private readonly ToolStripMenuItem _statusMicItem;
    private readonly ToolStripMenuItem _blockCameraItem;
    private readonly ToolStripMenuItem _blockMicItem;
    private readonly ToolStripMenuItem _blockBothItem;
    private readonly ToolStripMenuItem _unblockCameraItem;
    private readonly ToolStripMenuItem _unblockMicItem;
    private readonly ToolStripMenuItem _unblockBothItem;
    private readonly ToolStripMenuItem _startupItem;

    // Icons (tracked for disposal)
    private Icon? _greenIcon;
    private Icon? _redIcon;
    private Icon? _yellowIcon;

    /// <summary>Fired when the user clicks Exit.</summary>
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

        // Status section (disabled items, for display only)
        _statusCameraItem = new ToolStripMenuItem("Camera: Checking...") { Enabled = false };
        _statusMicItem = new ToolStripMenuItem("Microphone: Checking...") { Enabled = false };
        _contextMenu.Items.Add(_statusCameraItem);
        _contextMenu.Items.Add(_statusMicItem);
        _contextMenu.Items.Add(new ToolStripSeparator());

        // Block actions
        _blockCameraItem = new ToolStripMenuItem("🔒 Block Camera");
        _blockCameraItem.Click += async (_, _) => await SafeExecuteAsync(() => _blockingService.BlockAsync(BlockTarget.Camera));

        _blockMicItem = new ToolStripMenuItem("🔒 Block Microphone");
        _blockMicItem.Click += async (_, _) => await SafeExecuteAsync(() => _blockingService.BlockAsync(BlockTarget.Microphone));

        _blockBothItem = new ToolStripMenuItem("🔒 Block Both");
        _blockBothItem.Click += async (_, _) => await SafeExecuteAsync(() => _blockingService.BlockAsync(BlockTarget.Both));

        _contextMenu.Items.Add(_blockCameraItem);
        _contextMenu.Items.Add(_blockMicItem);
        _contextMenu.Items.Add(_blockBothItem);
        _contextMenu.Items.Add(new ToolStripSeparator());

        // Unblock actions
        _unblockCameraItem = new ToolStripMenuItem("🔓 Unblock Camera");
        _unblockCameraItem.Click += async (_, _) => await SafeExecuteAsync(() => _blockingService.UnblockAsync(BlockTarget.Camera));

        _unblockMicItem = new ToolStripMenuItem("🔓 Unblock Microphone");
        _unblockMicItem.Click += async (_, _) => await SafeExecuteAsync(() => _blockingService.UnblockAsync(BlockTarget.Microphone));

        _unblockBothItem = new ToolStripMenuItem("🔓 Unblock Both");
        _unblockBothItem.Click += async (_, _) => await SafeExecuteAsync(() => _blockingService.UnblockAsync(BlockTarget.Both));

        _contextMenu.Items.Add(_unblockCameraItem);
        _contextMenu.Items.Add(_unblockMicItem);
        _contextMenu.Items.Add(_unblockBothItem);
        _contextMenu.Items.Add(new ToolStripSeparator());

        // Settings section
        _startupItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = _startupService.IsStartupEnabled()
        };
        _startupItem.Click += (_, _) =>
        {
            if (_startupItem.Checked)
                _startupService.EnableStartup();
            else
                _startupService.DisableStartup();
        };
        _contextMenu.Items.Add(_startupItem);

        // Log folder
        var openLogItem = new ToolStripMenuItem("📁 Open Log Folder");
        openLogItem.Click += (_, _) =>
        {
            var logDir = LoggingConfiguration.GetLogDirectory();
            if (System.IO.Directory.Exists(logDir))
                System.Diagnostics.Process.Start("explorer.exe", logDir);
        };
        _contextMenu.Items.Add(openLogItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // Hotkey hint
        var hotkeyItem = new ToolStripMenuItem("Hotkey: Ctrl + Alt + B") { Enabled = false };
        _contextMenu.Items.Add(hotkeyItem);

        // Exit
        var exitItem = new ToolStripMenuItem("❌ Exit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();
        _contextMenu.Items.Add(exitItem);

        // Create NotifyIcon
        _notifyIcon = new NotifyIcon
        {
            Icon = _greenIcon,
            Text = "CamMicBlocker — Checking...",
            Visible = true,
            ContextMenuStrip = _contextMenu
        };

        // Left click toggles
        _notifyIcon.MouseClick += async (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                await SafeExecuteAsync(() => _blockingService.ToggleAsync(BlockTarget.Both));
            }
        };

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
        // Update status display
        _statusCameraItem.Text = $"Camera: {FormatStatus(state.Camera.EffectiveStatus)}";
        _statusMicItem.Text = $"Microphone: {FormatStatus(state.Microphone.EffectiveStatus)}";

        // Update icon
        if (state.AllBlocked)
        {
            _notifyIcon.Icon = _redIcon;
            _notifyIcon.Text = "CamMicBlocker — Camera & Microphone: BLOCKED";
        }
        else if (state.AllAllowed)
        {
            _notifyIcon.Icon = _greenIcon;
            _notifyIcon.Text = "CamMicBlocker — Camera & Microphone: Allowed";
        }
        else
        {
            _notifyIcon.Icon = _yellowIcon;
            _notifyIcon.Text = "CamMicBlocker — Partial/Mixed state";
        }

        // Update menu item enabled states
        var cameraBlocked = state.Camera.EffectiveStatus == BlockStatus.Blocked;
        var micBlocked = state.Microphone.EffectiveStatus == BlockStatus.Blocked;

        _blockCameraItem.Enabled = !cameraBlocked;
        _blockMicItem.Enabled = !micBlocked;
        _blockBothItem.Enabled = !(cameraBlocked && micBlocked);
        _unblockCameraItem.Enabled = cameraBlocked;
        _unblockMicItem.Enabled = micBlocked;
        _unblockBothItem.Enabled = cameraBlocked || micBlocked;

        Log.Debug("UI updated: Camera={CameraState}, Mic={MicState}",
            state.Camera.EffectiveStatus, state.Microphone.EffectiveStatus);
    }

    /// <summary>
    /// Shows a balloon notification from the tray icon.
    /// </summary>
    public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon.ShowBalloonTip(2000, title, message, icon);
    }

    private static string FormatStatus(BlockStatus status) => status switch
    {
        BlockStatus.Allowed => "✅ Allowed",
        BlockStatus.Blocked => "🔒 Blocked",
        BlockStatus.Unknown => "⚠️ Unknown",
        _ => "?"
    };

    private async Task SafeExecuteAsync(Func<Task<Domain.Interfaces.OperationResult>> action)
    {
        try
        {
            var result = await action();
            if (!result.Success)
            {
                ShowNotification("CamMicBlocker", $"Operation failed: {result.ErrorMessage}", ToolTipIcon.Error);
            }
            else
            {
                var state = _blockingService.GetCurrentState();
                var statusText = state.AllBlocked ? "BLOCKED" : state.AllAllowed ? "Allowed" : "Partially blocked";
                ShowNotification("CamMicBlocker", $"Camera & Microphone: {statusText}");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error executing tray action");
            ShowNotification("CamMicBlocker", $"Error: {ex.Message}", ToolTipIcon.Error);
        }
    }

    /// <summary>
    /// Creates a padlock icon in the specified color. Generated in memory — no external file needed.
    /// Properly manages GDI handle lifecycle.
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

        // Create icon from bitmap — GetHicon() creates a native handle
        var hIcon = bmp.GetHicon();
        // Icon.FromHandle doesn't take ownership; we create a clone that does
        var tempIcon = Icon.FromHandle(hIcon);
        var icon = (Icon)tempIcon.Clone();
        tempIcon.Dispose();
        DestroyIcon(hIcon); // Clean up the native handle

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
