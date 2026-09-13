using System.Security.Claims;
using OpenOnboarding.Api.Authentication;

namespace OpenOnboarding.Api.RateLimiting;

/// <summary>
/// Derives the key that decides whose budget a request spends.
/// <para>
/// A rate limit without a partition key is a shared bucket: one caller exhausting it locks out
/// every other caller. That is a denial of service against <c>session-start</c> in particular,
/// which is anonymous by design and is where every journey begins.
/// </para>
/// <para>
/// Every key is prefixed by the kind of thing it names, so an address can never collide with a
/// session id or a principal id that happens to have the same text.
/// </para>
/// </summary>
public static class RateLimitPartitionKeys
{
    /// <summary>Key used when a request has no address at all - a unix socket or an in-process test host.</summary>
    public const string Unknown = "addr:unknown";

    /// <summary>
    /// The caller's network address, after <c>UseForwardedHeaders</c> has resolved
    /// <c>X-Forwarded-For</c> from a trusted proxy. A header from anywhere else is ignored, so a
    /// caller cannot rotate it to mint fresh budgets.
    /// </summary>
    public static string ClientAddress(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        return address is null ? Unknown : $"addr:{address}";
    }

    /// <summary>
    /// The authenticated principal, falling back to the client address while a request is still
    /// anonymous - the limiter runs before authorization, so unauthenticated calls reach it too.
    /// </summary>
    public static string Principal(HttpContext context)
    {
        var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? context.User.Identity?.Name;

        return string.IsNullOrWhiteSpace(id) ? ClientAddress(context) : $"user:{id}";
    }

    /// <summary>
    /// The applicant session the caller's token names, so one applicant's browser cannot throttle
    /// another's. Operators carry no session claim and fall back to their own principal.
    /// </summary>
    public static string ApplicantSession(HttpContext context)
    {
        var sessionId = context.User.FindFirstValue(ApplicantSessionAuthenticationDefaults.SessionIdClaim);

        return string.IsNullOrWhiteSpace(sessionId) ? Principal(context) : $"session:{sessionId}";
    }
}
