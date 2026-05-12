using Microsoft.AspNetCore.Mvc;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Interfaces;

namespace OpenOnboarding.Api.Controllers;

[ApiController]
[Route("api/workflow")]
public sealed class WorkflowController(IWorkflowService workflowService) : ControllerBase
{
    [HttpPost("sessions/start")]
    [ProducesResponseType(typeof(SessionStepResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SessionStepResponse>> StartSession([FromBody] StartSessionRequest request, CancellationToken cancellationToken)
    {
        var result = await workflowService.StartSessionAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("sessions/{sessionId:guid}/steps/{nodeId:guid}/submit")]
    [ProducesResponseType(typeof(SessionStepResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SessionStepResponse>> SubmitStep([FromRoute] Guid sessionId, [FromRoute] Guid nodeId, [FromBody] SubmitStepRequest request, CancellationToken cancellationToken)
    {
        var result = await workflowService.SubmitStepAsync(sessionId, nodeId, request, cancellationToken);
        return Ok(result);
    }

    [HttpGet("sessions/{sessionId:guid}/next")]
    [ProducesResponseType(typeof(SessionStepResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SessionStepResponse>> GetNextStep([FromRoute] Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await workflowService.GetNextStepAsync(sessionId, cancellationToken);
        return Ok(result);
    }
}
