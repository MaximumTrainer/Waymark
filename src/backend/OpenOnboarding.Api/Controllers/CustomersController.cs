using Microsoft.AspNetCore.Mvc;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Interfaces;

namespace OpenOnboarding.Api.Controllers;

/// <summary>
/// Manages customer profiles.
/// </summary>
[ApiController]
[Route("api/customers")]
[Produces("application/json")]
public sealed class CustomersController(ICustomerService customerService) : ControllerBase
{
    /// <summary>
    /// Creates a new customer profile.
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CustomerProfileDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerProfileDto>> Create([FromBody] CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        var result = await customerService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>
    /// Returns a customer profile by internal ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CustomerProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerProfileDto>> GetById([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var result = await customerService.GetByIdAsync(id, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Returns a customer profile by external customer ID.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(CustomerProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerProfileDto>> GetByExternalId([FromQuery] string externalId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation failed",
                Detail = "externalId must not be empty."
            });
        }

        var result = await customerService.GetByExternalIdAsync(externalId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Updates an existing customer profile's country, email, and metadata.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CustomerProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerProfileDto>> Update([FromRoute] Guid id, [FromBody] UpdateCustomerRequest request, CancellationToken cancellationToken)
    {
        var result = await customerService.UpdateAsync(id, request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Deletes a customer profile. Returns 409 if the profile has active sessions.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        await customerService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
