using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenOnboarding.Application.Tests.TestHelpers;
using OpenOnboarding.Infrastructure.Services;

namespace OpenOnboarding.Application.Tests;

public sealed class NullVirusScanServiceTests
{
    [Fact]
    public async Task ScanAsync_ReturnsCleanResult()
    {
        var metrics = new NoOpMetricsService();
        var sut = new NullVirusScanService(NullLogger<NullVirusScanService>.Instance, metrics);

        var result = await sut.ScanAsync(Stream.Null);

        Assert.True(result.IsSafe);
        Assert.Null(result.ThreatName);
    }

    [Fact]
    public async Task ScanAsync_IncrementsBypassedCounter()
    {
        var metrics = new CountingMetricsService();
        var sut = new NullVirusScanService(NullLogger<NullVirusScanService>.Instance, metrics);

        await sut.ScanAsync(Stream.Null);
        await sut.ScanAsync(Stream.Null);

        Assert.Equal(2, metrics.VirusScanBypassedCount);
    }

    [Fact]
    public async Task ScanAsync_LogsWarning_EachCall()
    {
        var logger = new CapturingLogger<NullVirusScanService>();
        var sut = new NullVirusScanService(logger, new NoOpMetricsService());

        await sut.ScanAsync(Stream.Null);

        Assert.Contains(logger.Messages, m => m.Level == LogLevel.Warning && m.Message.Contains("bypassed"));
    }

    [Fact]
    public void Constructor_LogsStartupWarning()
    {
        var logger = new CapturingLogger<NullVirusScanService>();
        _ = new NullVirusScanService(logger, new NoOpMetricsService());

        Assert.Contains(logger.Messages, m =>
            m.Level == LogLevel.Warning &&
            m.Message.Contains("DISABLED", StringComparison.OrdinalIgnoreCase));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private sealed class CountingMetricsService : IMetricsService
    {
        public int VirusScanBypassedCount { get; private set; }
        public void IncrementSessionsStarted(string flowId) { }
        public void IncrementSessionsCompleted(string flowId) { }
        public void IncrementWebhookDeliveries(string status) { }
        public void SetActiveSessions(int count) { }
        public void IncrementVirusScanBypassed() => VirusScanBypassedCount++;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public record LogEntry(LogLevel Level, string Message);
        public List<LogEntry> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(new LogEntry(logLevel, formatter(state, exception)));
    }
}
