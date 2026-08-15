using PrivLock.Infrastructure.Common.Logging;
using Xunit;

namespace PrivLock.Infrastructure.Tests;

public class CrashReporterTests : IDisposable
{
    private readonly string _testCrashDir;

    public CrashReporterTests()
    {
        _testCrashDir = Path.Combine(Path.GetTempPath(), $"PrivLock_CrashTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testCrashDir);
    }

    [Fact]
    public void GenerateCrashReport_WritesValidJsonReport()
    {
        var ex = new InvalidOperationException("Test exception for crash report");
        var reportPath = CrashReporter.GenerateCrashReport(ex, "CrashReporterTests", new { Test = true }, _testCrashDir);

        Assert.True(File.Exists(reportPath));
        var content = File.ReadAllText(reportPath);
        Assert.Contains("Test exception for crash report", content);
        Assert.Contains("InvalidOperationException", content);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testCrashDir, recursive: true); }
        catch { /* Best effort */ }
    }
}
