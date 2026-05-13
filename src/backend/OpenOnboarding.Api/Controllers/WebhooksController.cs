using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using OpenOnboarding.Api.Validation;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Interfaces;

namespace OpenOnboarding.Api.Controllers;

/// <summary>
/// Manages webhook registrations and delivery history.
/// </summary>
[ApiController]
[Route("api/flows/{flowId:guid}")]
[Produces("application/json")]
[Authorize(Policy = "OperatorOnly")]
public sealed class WebhooksController(IWebhookService webhookService, IConfiguration configuration) : ControllerBase
{
    /// <summary>
    /// Registers a webhook URL for session-completed events on a flow.
    /// </summary>
    [HttpPost("webhooks")]
    [EnableRateLimiting("webhook-registration")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(WebhookDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WebhookDto>> RegisterWebhook(
        [FromRoute] Guid flowId,
        [FromBody] WebhookRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        var allowPrivate = configuration.GetValue<bool>("Webhooks:AllowPrivateNetworks");
        if (!WebhookUrlValidator.IsValidPublicUrl(request.Url, allowPrivate))
        {
            return BadRequest(new ProblemDetails
            {
                Status = 400,
                Title = "Invalid webhook URL",
                Detail = "Webhook URL must be a publicly reachable HTTP or HTTPS endpoint."
            });
        }

        var result = await webhookService.RegisterAsync(flowId, request.Url, request.Secret, cancellationToken);
        return CreatedAtAction(nameof(ListWebhooks), new { flowId }, result);
    }

    /// <summary>
    /// Lists all webhooks registered for a flow.
    /// </summary>
    [HttpGet("webhooks")]
    [ProducesResponseType(typeof(IReadOnlyList<WebhookDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WebhookDto>>> ListWebhooks(
        [FromRoute] Guid flowId,
        CancellationToken cancellationToken)
    {
        var result = await webhookService.ListAsync(flowId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Deletes a registered webhook.
    /// </summary>
    [HttpDelete("webhooks/{webhookId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteWebhook(
        [FromRoute] Guid flowId,
        [FromRoute] Guid webhookId,
        CancellationToken cancellationToken)
    {
        await webhookService.DeleteAsync(flowId, webhookId, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Returns the webhook delivery log for a flow.
    /// </summary>
    [HttpGet("webhook-deliveries")]
    [ProducesResponseType(typeof(IReadOnlyList<WebhookDeliveryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WebhookDeliveryDto>>> ListWebhookDeliveries(
        [FromRoute] Guid flowId,
        CancellationToken cancellationToken)
    {
        var result = await webhookService.ListDeliveriesAsync(flowId, cancellationToken);
        return Ok(result);
    }
}

public sealed record WebhookRegistrationRequest(string Url, string Secret);
