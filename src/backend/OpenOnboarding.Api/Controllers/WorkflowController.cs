using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using OpenOnboarding.Api.Authorization;
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
[Authorize]
public sealed class WorkflowController(
    IWorkflowService workflowService,
    ISessionAnalyticsService sessionAnalyticsService,
    IAuthorizationService authorizationService,
    ISessionEventEmitter sessionEventEmitter,
    IConfiguration configuration) : ControllerBase
{
    /// <summary>
    /// Starts a new onboarding session for the given workflow flow.
    /// </summary>
    [HttpPost("sessions/start")]
    [EnableRateLimiting("session-start")]
    [Authorize(Policy = "ApplicantOrOperator")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(SessionStepResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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
    [Authorize(Policy = "ApplicantOrOperator")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(SessionStepResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SessionStepResponse>> SubmitStep([FromRoute] Guid sessionId, [FromRoute] Guid nodeId, [FromBody] SubmitStepRequest request, CancellationToken cancellationToken)
    {
        var session = await sessionAnalyticsService.GetSessionAsync(sessionId, cancellationToken);
        var authResult = await authorizationService.AuthorizeAsync(User, session, new SessionOwnershipRequirement());
        if (!authResult.Succeeded) return Forbid();

        var result = await workflowService.SubmitStepAsync(sessionId, nodeId, request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves the next pending step for an existing onboarding session.
    /// </summary>
    [HttpGet("sessions/{sessionId:guid}/next")]
    [Authorize(Policy = "ApplicantOrOperator")]
    [ProducesResponseType(typeof(SessionStepResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SessionStepResponse>> GetNextStep([FromRoute] Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await sessionAnalyticsService.GetSessionAsync(sessionId, cancellationToken);
        var authResult = await authorizationService.AuthorizeAsync(User, session, new SessionOwnershipRequirement());
        if (!authResult.Succeeded) return Forbid();

        var result = await workflowService.GetNextStepAsync(sessionId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Abandons an active onboarding session. Idempotent if already abandoned.
    /// Returns 409 Conflict if the session is already completed.
    /// </summary>
    [HttpDelete("sessions/{sessionId:guid}")]
    [Authorize(Policy = "ApplicantOrOperator")]
    [ProducesResponseType(typeof(SessionStepResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SessionStepResponse>> AbandonSession([FromRoute] Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await sessionAnalyticsService.GetSessionAsync(sessionId, cancellationToken);
        var authResult = await authorizationService.AuthorizeAsync(User, session, new SessionOwnershipRequirement());
        if (!authResult.Succeeded) return Forbid();

        var result = await workflowService.AbandonSessionAsync(sessionId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Returns all submissions for a session in chronological order.
    /// </summary>
    [HttpGet("sessions/{sessionId:guid}/submissions")]
    [Authorize(Policy = "OperatorOnly")]
    [ProducesResponseType(typeof(IReadOnlyList<SubmissionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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
    [Authorize(Policy = "ApplicantOrOperator")]
    [ProducesResponseType(typeof(SessionDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SessionDetailDto>> GetSession([FromRoute] Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await sessionAnalyticsService.GetSessionAsync(sessionId, cancellationToken);
        var authResult = await authorizationService.AuthorizeAsync(User, session, new SessionOwnershipRequirement());
        if (!authResult.Succeeded) return Forbid();

        return Ok(session);
    }

    /// <summary>
    /// Returns a paginated list of sessions, optionally filtered by flow and/or status.
    /// <paramref name="pageSize"/> is capped at 100.
    /// </summary>
    [HttpGet("sessions")]
    [Authorize(Policy = "OperatorOnly")]
    [ProducesResponseType(typeof(PaginatedResult<SessionListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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
    [Authorize(Policy = "OperatorOnly")]
    [ProducesResponseType(typeof(FlowStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<FlowStatsDto>> GetFlowStats([FromRoute] Guid flowId, CancellationToken cancellationToken)
    {
        var result = await sessionAnalyticsService.GetFlowStatsAsync(flowId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Uploads one or more documents for a DocumentUpload node.
    /// </summary>
    [HttpPost("sessions/{sessionId:guid}/steps/{nodeId:guid}/documents")]
    [Authorize(Policy = "ApplicantOrOperator")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(IReadOnlyList<StoredFileInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status413RequestEntityTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> UploadDocuments(
        [FromRoute] Guid sessionId,
        [FromRoute] Guid nodeId,
        IList<IFormFile> files,
        CancellationToken cancellationToken)
    {
        if (files == null || files.Count == 0)
            return BadRequest(new ProblemDetails { Title = "No files provided.", Status = 400 });

        var maxFileSizeBytes = configuration.GetValue<long>("DocumentUpload:MaxFileSizeBytes", 10_485_760);

        var items = files.Select(f => new DocumentUploadItem(f.OpenReadStream(), f.FileName, f.ContentType, f.Length)).ToList();

        try
        {
            var stored = await workflowService.UploadDocumentsAsync(sessionId, nodeId, items, maxFileSizeBytes, cancellationToken);
            return Ok(stored);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("FILE_TOO_LARGE:"))
        {
            var fileName = ex.Message["FILE_TOO_LARGE:".Length..];
            return StatusCode(StatusCodes.Status413RequestEntityTooLarge, new ProblemDetails
            {
                Title = $"File '{fileName}' exceeds the maximum allowed size.",
                Status = 413
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("UNSUPPORTED_MEDIA_TYPE:"))
        {
            var mimeType = ex.Message["UNSUPPORTED_MEDIA_TYPE:".Length..];
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new ProblemDetails
            {
                Title = $"File type '{mimeType}' is not accepted.",
                Status = 415
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message, Status = 400 });
        }
        finally
        {
            foreach (var item in items) await item.Stream.DisposeAsync();
        }
    }

    /// <summary>
    /// Downloads a previously uploaded document.
    /// </summary>
    [HttpGet("sessions/{sessionId:guid}/steps/{nodeId:guid}/documents/{fileId}")]
    [Authorize(Policy = "ApplicantOrOperator")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDocument(
        [FromRoute] Guid sessionId,
        [FromRoute] Guid nodeId,
        [FromRoute] string fileId,
        CancellationToken cancellationToken)
    {
        var (stream, info) = await workflowService.GetDocumentAsync(fileId, cancellationToken);
        return File(stream, info.ContentType, info.FileName);
    }

    /// <summary>
    /// Streams server-sent events for a session (step-advanced, session-completed, session-abandoned).
    /// </summary>
    [HttpGet("sessions/{sessionId:guid}/events")]
    [Authorize(Policy = "ApplicantOrOperator")]
    public async Task StreamEvents([FromRoute] Guid sessionId, CancellationToken cancellationToken)
    {
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        await foreach (var evt in sessionEventEmitter.SubscribeAsync(sessionId, cancellationToken))
        {
            await Response.WriteAsync($"event: {evt.EventType}\n", cancellationToken);
            await Response.WriteAsync($"data: {evt.PayloadJson}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }
}
