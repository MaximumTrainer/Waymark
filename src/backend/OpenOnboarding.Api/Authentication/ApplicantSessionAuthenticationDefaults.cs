namespace OpenOnboarding.Api.Authentication;

public static class ApplicantSessionAuthenticationDefaults
{
    public const string SchemeName = "ApplicantSession";

    /// <summary>Issuer stamped on tokens this API mints, used to tell them from external IdP tokens.</summary>
    public const string Issuer = "waymark-applicant";

    public const string Audience = "waymark-applicant-session";

    /// <summary>Claim carrying the single session the token is allowed to act on.</summary>
    public const string SessionIdClaim = "sessionId";

    public const string CustomerProfileIdClaim = "customerProfileId";

    /// <summary>
    /// Query parameter accepted in place of the Authorization header. The browser EventSource API
    /// cannot set request headers, so the SSE endpoint has no other way to present a credential.
    /// </summary>
    public const string QueryParameterName = "access_token";
}
