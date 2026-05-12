using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace OpenOnboarding.Api.Controllers;

/// <summary>
/// Provides authentication diagnostics and identity introspection.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    /// <summary>
    /// Returns the current principal's claims.
    /// Always accessible (even without authentication) for debugging purposes.
    /// </summary>
    [HttpGet("me")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Me()
    {
        if (User.Identity?.IsAuthenticated != true)
            return Ok(new { authenticated = false });

        return Ok(new
        {
            authenticated = true,
            sub = User.FindFirstValue(ClaimTypes.NameIdentifier),
            name = User.FindFirstValue(ClaimTypes.Name),
            email = User.FindFirstValue(ClaimTypes.Email),
            roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray()
        });
    }
}
