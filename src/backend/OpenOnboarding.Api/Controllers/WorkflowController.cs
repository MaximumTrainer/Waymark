using Microsoft.AspNetCore.Mvc;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Domain.Enums;

namespace OpenOnboarding.Api.Controllers;

/// <summary>
/// Manages onboarding workflow sessions and step progression.
/// </summary>
[ApiController]
[Route("api/workflow")]
[Produces("application/json")]
public sealed class WorkflowController(
    IWorkflowService workflowService,
    ISessionAnalyticsService sessionAnalyticsService) : ControllerBase
{
    /// <summary>
    /// Starts a new onboarding session for the given workflow flow.
    /// </summary>
    [HttpPost("sessions/start")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(SessionStepResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SessionStepResponse>> StartSession([FromBody] StartSessionRequest request, CancellationToken cancellationToken)
    {
        var result = await workflowService.StartSessionAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Submits the response for the current step in an onboarding session.
    /// </summary>
    [HttpPost("sessions/{sessionId:guid}/steps/{nodeId:guid}/submit")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(SessionStepResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SessionStepResponse>> SubmitStep([FromRoute] Guid sessionId, [FromRoute] Guid nodeId, [FromBody] SubmitStepRequest request, CancellationToken cancellationToken)
    {
        var result = await workflowService.SubmitStepAsync(sessionId, nodeId, request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves the next pending step for an existing onboarding session.
    /// </summary>
    [HttpGet("sessions/{sessionId:guid}/next")]
    [ProducesResponseType(typeof(SessionStepResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SessionStepResponse>> GetNextStep([FromRoute] Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await workflowService.GetNextStepAsync(sessionId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Abandons an active onboarding session. Idempotent if already abandoned.
    /// Returns 409 Conflict if the session is already completed.
    /// </summary>
    [HttpDelete("sessions/{sessionId:guid}")]
    [ProducesResponseType(typeof(SessionStepResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SessionStepResponse>> AbandonSession([FromRoute] Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await workflowService.AbandonSessionAsync(sessionId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Returns all submissions for a session in chronological order.
    /// </summary>
    [HttpGet("sessions/{sessionId:guid}/submissions")]
    [ProducesResponseType(typeof(IReadOnlyList<SubmissionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IReadOnlyList<SubmissionDto>>> GetSubmissions([FromRoute] Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await sessionAnalyticsService.GetSubmissionsAsync(sessionId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Returns full detail for a single session including status, timestamps, and current node.
    /// </summary>
    [HttpGet("sessions/{sessionId:guid}")]
    [ProducesResponseType(typeof(SessionDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SessionDetailDto>> GetSession([FromRoute] Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await sessionAnalyticsService.GetSessionAsync(sessionId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Returns a paginated list of sessions, optionally filtered by flow and/or status.
    /// <paramref name="pageSize"/> is capped at 100.
    /// </summary>
    [HttpGet("sessions")]
    [ProducesResponseType(typeof(PaginatedResult<SessionListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PaginatedResult<SessionListItemDto>>> GetSessions(
        [FromQuery] Guid? flowId,
        [FromQuery] SessionStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await sessionAnalyticsService.GetSessionsAsync(flowId, status, page, pageSize, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Returns aggregate analytics for a workflow flow.
    /// </summary>
    [HttpGet("flows/{flowId:guid}/stats")]
    [ProducesResponseType(typeof(FlowStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<FlowStatsDto>> GetFlowStats([FromRoute] Guid flowId, CancellationToken cancellationToken)
    {
        var result = await sessionAnalyticsService.GetFlowStatsAsync(flowId, cancellationToken);
        return Ok(result);
    }
}

