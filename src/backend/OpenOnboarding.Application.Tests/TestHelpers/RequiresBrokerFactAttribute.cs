using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace OpenOnboarding.Application.Tests.TestHelpers;

/// <summary>
/// A <see cref="FactAttribute"/> that skips itself when no RabbitMQ broker is reachable.
/// <para>
/// The same bargain the git hooks make: <c>dotnet test</c> must pass on any machine with the SDKs
/// installed, while the suites that need real infrastructure still run where it exists - CI, or a
/// developer with <c>docker compose up</c>. Skipping is decided at discovery so the reachability
/// probe runs once per test class rather than once per test.
/// </para>
/// </summary>
public sealed class RequiresBrokerFactAttribute : FactAttribute
{
    public RequiresBrokerFactAttribute()
    {
        if (!BrokerProbe.IsReachable && !BrokerProbe.BrokerIsRequired)
            Skip = BrokerProbe.SkipReason;
    }
}

/// <summary>
/// As <see cref="RequiresBrokerFactAttribute"/>, but also needs the broker's management HTTP API.
/// <para>
/// Only the connection-recovery test needs it: a dropped connection has to be dropped by the
/// broker, because the client treats an application-initiated close as deliberate and does not
/// recover from one. The management API is how you ask the broker to do that.
/// </para>
/// </summary>
public sealed class RequiresBrokerManagementFactAttribute : FactAttribute
{
    public RequiresBrokerManagementFactAttribute()
    {
        if (BrokerProbe.BrokerIsRequired)
            return;

        if (!BrokerProbe.IsReachable)
            Skip = BrokerProbe.SkipReason;
        else if (!BrokerProbe.IsManagementReachable)
            Skip = BrokerProbe.ManagementSkipReason;
    }
}

/// <summary>
/// Resolves the broker to test against and reports whether it answers.
/// </summary>
public static class BrokerProbe
{
    /// <summary>Environment variable naming the broker, so CI can point at its service container.</summary>
    public const string UriVariable = "SESSIONEVENTS__RABBITMQ__URI";

    /// <summary>Port the management plugin listens on. Only the recovery test needs it.</summary>
    public const int ManagementPort = 15672;

    /// <summary>
    /// Set where a broker is guaranteed to exist - CI, which declares one as a service container.
    /// There, a missing broker must fail the build rather than skip: a silently skipped test is a
    /// green build that proved nothing, which is how this adapter came to have no coverage at all.
    /// </summary>
    public const string RequiredVariable = "REQUIRE_BROKER_TESTS";

    /// <summary>Whether a missing broker should fail instead of skip.</summary>
    public static bool BrokerIsRequired =>
        Environment.GetEnvironmentVariable(RequiredVariable) is "1" or "true";

    private static readonly Lazy<bool> Reachable = new(() => Probe(AmqpPort), isThreadSafe: true);
    private static readonly Lazy<bool> ManagementReachable =
        new(() => Probe(ManagementPort), isThreadSafe: true);

    private const int AmqpPort = 5672;

    /// <summary>The broker URI under test, defaulting to a local one.</summary>
    public static string BrokerUri =>
        Environment.GetEnvironmentVariable(UriVariable) is { Length: > 0 } configured
            ? configured
            : "amqp://guest:guest@localhost:5672/";

    public static bool IsReachable => Reachable.Value;

    public static bool IsManagementReachable => ManagementReachable.Value;

    public static string SkipReason =>
        $"No RabbitMQ broker reachable at {BrokerUri}. Start one (docker compose up rabbitmq) " +
        $"or set {UriVariable} to run this test.";

    public static string ManagementSkipReason =>
        $"The RabbitMQ broker at {BrokerUri} has no management API on port {ManagementPort}. " +
        "Run an image that includes it (rabbitmq:3-management) to run this test.";

    /// <summary>
    /// Asks the broker to close the connection with the given client-provided name, so the client
    /// sees a failure and runs its recovery path.
    /// </summary>
    public static async Task ForceCloseConnectionAsync(string clientProvidedName)
    {
        var broker = new Uri(BrokerUri);
        var credentials = broker.UserInfo.Split(':', 2);

        using var http = new HttpClient
        {
            BaseAddress = new UriBuilder("http", broker.Host, ManagementPort).Uri,
            Timeout = TimeSpan.FromSeconds(10)
        };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{Uri.UnescapeDataString(credentials.ElementAtOrDefault(0) ?? "guest")}:" +
                $"{Uri.UnescapeDataString(credentials.ElementAtOrDefault(1) ?? "guest")}")));

        // The connection may take a moment to register in the management database.
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            using var listing = await http.GetAsync("/api/connections");
            listing.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(await listing.Content.ReadAsStringAsync());
            var match = document.RootElement.EnumerateArray().FirstOrDefault(connection =>
                connection.TryGetProperty("client_properties", out var properties)
                && properties.TryGetProperty("connection_name", out var name)
                && name.GetString() == clientProvidedName);

            if (match.ValueKind == JsonValueKind.Object)
            {
                var id = match.GetProperty("name").GetString()!;
                using var delete = await http.DeleteAsync($"/api/connections/{Uri.EscapeDataString(id)}");
                delete.EnsureSuccessStatusCode();
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new InvalidOperationException(
            $"The broker never reported a connection named {clientProvidedName}.");
    }

    private static bool Probe(int defaultPort)
    {
        try
        {
            var uri = new Uri(BrokerUri);
            var port = defaultPort == AmqpPort && uri.Port > 0 ? uri.Port : defaultPort;

            using var client = new TcpClient();
            // Short: a missing broker is the normal case locally and must not slow the suite down.
            return client.ConnectAsync(uri.Host, port).Wait(TimeSpan.FromSeconds(2))
                   && client.Connected;
        }
        catch
        {
            return false;
        }
    }
}
