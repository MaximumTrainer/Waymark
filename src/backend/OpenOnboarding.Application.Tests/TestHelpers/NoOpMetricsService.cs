using OpenOnboarding.Application.Interfaces;

namespace OpenOnboarding.Application.Tests.TestHelpers;

internal sealed class NoOpMetricsService : IMetricsService
{
    public void IncrementSessionsStarted(string flowId) { }
    public void IncrementSessionsCompleted(string flowId) { }
    public void IncrementWebhookDeliveries(string status) { }
    public void SetActiveSessions(int count) { }
    public void IncrementVirusScanBypassed() { }
}
