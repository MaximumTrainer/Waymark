using PactNet;
using PactNet.Verifier;
using OpenOnboarding.Pact.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace OpenOnboarding.Pact.Tests.Providers;

/// <summary>
/// Verifies the open-onboarding-api satisfies Pact contracts defined by consumers.
///
/// To run against an external provider instead of the local one, set environment variable:
///   PACT_PROVIDER_URL=https://api.example.com
/// </summary>
public sealed class WorkflowProviderTests(ITestOutputHelper output)
{
    private static readonly string PactDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "frontend", "pacts"));

    [Fact]
    public async Task OpenOnboardingApi_HonoursWorkflowConsumerPact()
    {
        var externalProviderUrl = Environment.GetEnvironmentVariable("PACT_PROVIDER_URL");

        if (externalProviderUrl is not null)
        {
            VerifyAgainstExternalProvider(externalProviderUrl);
            return;
        }

        using var fixture = new PactProviderFixture();
        fixture.CreateClient(); // Trigger server startup

        var (flowId, nodeId) = await fixture.SeedFlowAsync();
        await fixture.SeedSessionAsync(flowId, nodeId);

        var pactFile = Path.Combine(PactDir, "open-onboarding-frontend-open-onboarding-api.json");
        if (!File.Exists(pactFile))
        {
            output.WriteLine($"Pact file not found at {pactFile}. Run frontend pact tests first.");
            return;
        }

        var config = new PactVerifierConfig
        {
            Outputters = [new XunitOutput(output)]
        };

        new PactVerifier("open-onboarding-api", config)
            .WithHttpEndpoint(fixture.Server.BaseAddress)
            .WithFileSource(new FileInfo(pactFile))
            .Verify();
    }

    private void VerifyAgainstExternalProvider(string providerUrl)
    {
        var pactFile = Path.Combine(PactDir, "open-onboarding-frontend-open-onboarding-api.json");
        if (!File.Exists(pactFile))
        {
            output.WriteLine($"Pact file not found at {pactFile}. Run frontend pact tests first.");
            return;
        }

        var config = new PactVerifierConfig
        {
            Outputters = [new XunitOutput(output)]
        };

        new PactVerifier("open-onboarding-api", config)
            .WithHttpEndpoint(new Uri(providerUrl))
            .WithFileSource(new FileInfo(pactFile))
            .Verify();

        output.WriteLine($"Verified pact against external provider: {providerUrl}");
    }
}
