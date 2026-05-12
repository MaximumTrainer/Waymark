using Microsoft.AspNetCore.Mvc;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Interfaces;

namespace OpenOnboarding.Api.Controllers;

/// <summary>
/// Manages onboarding workflow sessions and step progression.
/// </summary>
[ApiController]
[Route("api/workflow")]
[Produces("application/json")]
public sealed class WorkflowController(IWorkflowService workflowService) : ControllerBase
{
    /// <summary>
    /// Starts a new onboarding session for the given workflow flow.
    /// </summary>
    /// <param name="request">The flow ID and optional customer profile to start the session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The initial step of the session.</returns>
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
    /// <param name="sessionId">The unique identifier of the session.</param>
    /// <param name="nodeId">The unique identifier of the current workflow node/step.</param>
    /// <param name="request">The payload containing the step's answer data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The next step of the session, or completion status.</returns>
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
    /// <param name="sessionId">The unique identifier of the session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The next step of the session, or completion status.</returns>
    [HttpGet("sessions/{sessionId:guid}/next")]
    [ProducesResponseType(typeof(SessionStepResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SessionStepResponse>> GetNextStep([FromRoute] Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await workflowService.GetNextStepAsync(sessionId, cancellationToken);
        return Ok(result);
    }
}
