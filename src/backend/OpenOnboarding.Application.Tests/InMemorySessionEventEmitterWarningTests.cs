using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenOnboarding.Infrastructure.Services;

namespace OpenOnboarding.Application.Tests;

/// <summary>
/// The scale-out limitation of the in-memory emitter fails silently at runtime - the SSE stream
/// stays open and simply never delivers - so the startup warning is the only signal an operator
/// gets. It has to actually fire.
/// </summary>
public sealed class InMemorySessionEventEmitterWarningTests
{
    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "OpenOnboarding.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private static async Task<List<LogEntry>> StartInAsync(string environmentName)
    {
        var logger = new CapturingLogger<InMemorySessionEventEmitterWarning>();
        var service = new InMemorySessionEventEmitterWarning(new StubHostEnvironment(environmentName), logger);

        await service.StartAsync(CancellationToken.None);

        return logger.Entries;
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task NonDevelopmentEnvironment_WarnsAndNamesTheScaleOutLimitation(string environmentName)
    {
        var entries = await StartInAsync(environmentName);

        var warning = Assert.Single(entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains(environmentName, warning.Message);
        Assert.Contains("replica", warning.Message, StringComparison.OrdinalIgnoreCase);
        // The message has to say what to do about it, not just that something is wrong.
        Assert.Contains("SessionEvents:Transport", warning.Message);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task DevelopmentAndTesting_StaySilent(string environmentName)
    {
        // Single-instance by definition; a warning on every local run teaches people to ignore it.
        Assert.Empty(await StartInAsync(environmentName));
    }

    [Fact]
    public async Task StopAsync_IsANoOp()
    {
        var service = new InMemorySessionEventEmitterWarning(
            new StubHostEnvironment("Production"),
            new CapturingLogger<InMemorySessionEventEmitterWarning>());

        await service.StopAsync(CancellationToken.None);
    }
}
