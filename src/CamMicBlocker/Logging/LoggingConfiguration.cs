using System.IO;
using Serilog;
using Serilog.Events;

namespace CamMicBlocker.Logging;

/// <summary>
/// Configures Serilog for the application.
/// 
/// Log location: %LOCALAPPDATA%\CamMicBlocker\Logs\
/// Rolling daily, 7 day retention, max 10 MB per file.
/// </summary>
public static class LoggingConfiguration
{
    public static void Initialize()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CamMicBlocker", "Logs");

        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.WithProperty("Application", "CamMicBlocker")
            .WriteTo.File(
                path: Path.Combine(logDir, "CamMicBlocker-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 10 * 1024 * 1024, // 10 MB
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("=== CamMicBlocker starting ===");
        Log.Information("OS: {OS}", Environment.OSVersion);
        Log.Information("User: {User}", Environment.UserName);
        Log.Information(".NET: {Runtime}", Environment.Version);
        Log.Information("Log directory: {LogDir}", logDir);
    }

    /// <summary>
    /// Returns the path to the log directory for opening in Explorer.
    /// </summary>
    public static string GetLogDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CamMicBlocker", "Logs");
    }
}
