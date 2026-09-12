using Microsoft.Extensions.DependencyInjection;
using OpenOnboarding.Api.Authentication;
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

        var pactFile = Path.Combine(PactDir, "open-onboarding-frontend-open-onboarding-api.json");
        if (!File.Exists(pactFile))
        {
            output.WriteLine($"Pact file not found at {pactFile}. Run frontend pact tests first.");
            return;
        }

        using var fixture = new PactProviderFixture();

        var (flowId, nodeId) = await fixture.SeedFlowAsync();
        var sessionId = await fixture.SeedSessionAsync(flowId, nodeId);

        var config = new PactVerifierConfig
        {
            Outputters = [new XunitOutput(output)]
        };

        // Credentials are not part of a contract - the consumer's token is meaningless to this
        // provider. Mint a real one for the seeded session and inject it, which is the standard
        // Pact approach to authenticated interactions.
        var tokenService = fixture.Services.GetRequiredService<ApplicantSessionTokenService>();
        var (applicantToken, _) = tokenService.Issue(sessionId, customerProfileId: null);

        new PactVerifier("open-onboarding-api", config)
            .WithHttpEndpoint(fixture.ServerUri)
            .WithFileSource(new FileInfo(pactFile))
            .WithCustomHeader("Authorization", $"Bearer {applicantToken}")
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
