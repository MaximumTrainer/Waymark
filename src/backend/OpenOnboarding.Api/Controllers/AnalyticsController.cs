using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenOnboarding.Api.Authentication;
using OpenOnboarding.Api.Authorization;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Domain.Enums;

namespace OpenOnboarding.Api.Controllers;

/// <summary>
/// Ingests client-raised journey analytics and reads back the durable trail.
/// </summary>
[ApiController]
[Route("api/analytics")]
[Produces("application/json")]
[Authorize]
public sealed class AnalyticsController(
    ITelemetryService telemetryService,
    IAnalyticsEventStore analyticsEventStore,
    ISessionAnalyticsService sessionAnalyticsService,
    IFlowService flowService) : ControllerBase
{
    /// <summary>Most events a single batch may carry.</summary>
    public const int MaxBatchSize = 100;

    /// <summary>
    /// Records a batch of analytics events raised in the browser.
    /// </summary>
    /// <remarks>
    /// Every event must name the session the caller's own credential authorises, so a client cannot
    /// write events against someone else's journey. Operators may post for any session.
    /// </remarks>
    [HttpPost("events")]
    [EnableRateLimiting("analytics-ingest")]
    [Authorize(Policy = "ApplicantOrOperator")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AnalyticsIngestResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AnalyticsIngestResponse>> Ingest(
        [FromBody] AnalyticsIngestRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Events.Count == 0)
            return Ok(new AnalyticsIngestResponse { Accepted = 0 });

        if (request.Events.Count > MaxBatchSize)
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Batch too large",
                Detail = $"A batch may contain at most {MaxBatchSize} events."
            });

        var isOperator = User.IsInRole(AppRoles.Operator);
        var callerSessionId = User.FindFirst(ApplicantSessionAuthenticationDefaults.SessionIdClaim)?.Value;

        foreach (var incoming in request.Events)
        {
            if (string.IsNullOrWhiteSpace(incoming.EventType) ||
                string.IsNullOrWhiteSpace(incoming.SessionId) ||
                string.IsNullOrWhiteSpace(incoming.JourneyId))
            {
                return BadRequest(new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Invalid analytics event",
                    Detail = "eventType, journeyId and sessionId are required on every event."
                });
            }

            // An applicant token authorises one session; anything else in the batch is forged.
            if (!isOperator &&
                !string.Equals(incoming.SessionId, callerSessionId, StringComparison.OrdinalIgnoreCase))
            {
                return Forbid();
            }
        }

        // A token for a finished journey stops being accepted here. It survives until expiry - up
        // to a day - and on a shared machine that outlives the visit that created it. Operators are
        // unaffected: they post for sessions they did not start, finished ones included.
        if (!isOperator && Guid.TryParse(callerSessionId, out var callerSession))
        {
            var session = await sessionAnalyticsService.GetSessionAsync(callerSession, cancellationToken);
            if (session is not null && session.Status is SessionStatus.Completed or SessionStatus.Abandoned)
                return Forbid();
        }

        foreach (var incoming in request.Events)
        {
            await telemetryService.TrackAsync(
                new AnalyticsEvent
                {
                    EventId = incoming.EventId ?? Guid.NewGuid(),
                    EventType = incoming.EventType,
                    JourneyId = incoming.JourneyId,
                    SessionId = incoming.SessionId,
                    StepId = incoming.StepId,
                    StepIndex = incoming.StepIndex,
                    Payload = incoming.Payload,
                    OccurredAt = incoming.OccurredAt ?? DateTimeOffset.UtcNow,
                    // Stamped here, never taken from the caller: a client cannot pass its events
                    // off as server-raised.
                    Source = AnalyticsEventSources.Client
                },
                cancellationToken);
        }

        return Accepted(new AnalyticsIngestResponse { Accepted = request.Events.Count });
    }

    /// <summary>
    /// Returns one session's analytics trail in the order events occurred.
    /// </summary>
    [HttpGet("sessions/{sessionId:guid}/events")]
    [Authorize(Policy = "OperatorOnly")]
    [ProducesResponseType(typeof(IReadOnlyList<AnalyticsEvent>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AnalyticsEvent>>> GetSessionTrail(
        [FromRoute] Guid sessionId,
        CancellationToken cancellationToken)
        => Ok(await analyticsEventStore.GetSessionTrailAsync(sessionId.ToString(), cancellationToken));

    /// <summary>
    /// Returns aggregate performance figures for a flow.
    /// </summary>
    [HttpGet("flows/{flowId:guid}")]
    [Authorize(Policy = "OperatorOnly")]
    [ProducesResponseType(typeof(FlowAnalyticsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FlowAnalyticsDto>> GetFlowAnalytics(
        [FromRoute] Guid flowId,
        CancellationToken cancellationToken)
    {
        var flow = await flowService.GetFlowAsync(flowId, cancellationToken);
        var stats = await sessionAnalyticsService.GetFlowStatsAsync(flowId, cancellationToken);

        var topAbandonment = stats.DropOffByNodeKey
            .OrderByDescending(pair => pair.Value)
            .Select(pair => pair.Key)
            .FirstOrDefault();

        return Ok(new FlowAnalyticsDto
        {
            FlowId = flowId,
            FlowName = flow.Name,
            TotalSessions = stats.TotalSessions,
            CompletedSessions = stats.CompletedSessions,
            AbandonedSessions = stats.AbandonedSessions,
            AverageDurationSeconds = stats.AverageCompletionTimeSeconds,
            CompletionRate = stats.TotalSessions == 0
                ? 0
                : (double)stats.CompletedSessions / stats.TotalSessions,
            TopAbandonmentNodeTitle = topAbandonment
        });
    }
}
