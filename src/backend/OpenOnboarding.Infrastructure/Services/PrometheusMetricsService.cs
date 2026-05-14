using OpenOnboarding.Application.Interfaces;
using Prometheus;

namespace OpenOnboarding.Infrastructure.Services;

public sealed class PrometheusMetricsService : IMetricsService
{
    private static readonly Counter SessionsStarted = Metrics.CreateCounter(
        "onboarding_sessions_started_total", "Total onboarding sessions started.", "flowId");

    private static readonly Counter SessionsCompleted = Metrics.CreateCounter(
        "onboarding_sessions_completed_total", "Total onboarding sessions completed.", "flowId");

    private static readonly Counter WebhookDeliveries = Metrics.CreateCounter(
        "onboarding_webhook_deliveries_total", "Total webhook deliveries.", "status");

    private static readonly Gauge ActiveSessions = Metrics.CreateGauge(
        "onboarding_active_sessions", "Number of currently active onboarding sessions.");

    public void IncrementSessionsStarted(string flowId) => SessionsStarted.WithLabels(flowId).Inc();
    public void IncrementSessionsCompleted(string flowId) => SessionsCompleted.WithLabels(flowId).Inc();
    public void IncrementWebhookDeliveries(string status) => WebhookDeliveries.WithLabels(status).Inc();
    public void SetActiveSessions(int count) => ActiveSessions.Set(count);
}
