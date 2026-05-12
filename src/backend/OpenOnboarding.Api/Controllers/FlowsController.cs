using Microsoft.AspNetCore.Mvc;
using OpenOnboarding.Application.Contracts.Flows;
using OpenOnboarding.Application.Interfaces;

namespace OpenOnboarding.Api.Controllers;

/// <summary>
/// Manages flow definitions (CRUD operations).
/// </summary>
[ApiController]
[Route("api/flows")]
[Produces("application/json")]
public sealed class FlowsController(IFlowService flowService) : ControllerBase
{
    /// <summary>
    /// Creates a new flow with nodes and connections.
    /// </summary>
    /// <param name="request">The flow creation payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created flow including generated IDs.</returns>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(FlowDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<FlowDto>> CreateFlow([FromBody] CreateFlowRequest request, CancellationToken cancellationToken)
    {
        var result = await flowService.CreateFlowAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetFlow), new { flowId = result.Id }, result);
    }

    /// <summary>
    /// Returns a paginated list of flows.
    /// </summary>
    /// <param name="page">Page number (1-based, default 1).</param>
    /// <param name="pageSize">Number of items per page (default 20, max 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResult<FlowSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PaginatedResult<FlowSummaryDto>>> GetFlows(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await flowService.GetFlowsAsync(page, pageSize, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Returns the full flow including nodes and connections.
    /// </summary>
    /// <param name="flowId">The unique identifier of the flow.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("{flowId:guid}")]
    [ProducesResponseType(typeof(FlowDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FlowDto>> GetFlow([FromRoute] Guid flowId, CancellationToken cancellationToken)
    {
        var result = await flowService.GetFlowAsync(flowId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Replaces all nodes and connections of an existing flow and bumps the version.
    /// </summary>
    /// <param name="flowId">The unique identifier of the flow.</param>
    /// <param name="request">The updated flow payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPut("{flowId:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(FlowDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FlowDto>> UpdateFlow([FromRoute] Guid flowId, [FromBody] UpdateFlowRequest request, CancellationToken cancellationToken)
    {
        var result = await flowService.UpdateFlowAsync(flowId, request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Hard-deletes a flow. Returns 409 if the flow has active sessions.
    /// </summary>
    /// <param name="flowId">The unique identifier of the flow.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpDelete("{flowId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteFlow([FromRoute] Guid flowId, CancellationToken cancellationToken)
    {
        await flowService.DeleteFlowAsync(flowId, cancellationToken);
        return NoContent();
    }
}
