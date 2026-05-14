namespace OpenOnboarding.Application.Interfaces;

public interface IMetricsService
{
    void IncrementSessionsStarted(string flowId);
    void IncrementSessionsCompleted(string flowId);
    void IncrementWebhookDeliveries(string status);
    void SetActiveSessions(int count);
}
