using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenOnboarding.Api.Authorization;

namespace OpenOnboarding.Api.Authentication;

/// <summary>
/// Issues and validates the per-session credential handed to the applicant's browser when a session
/// starts.
/// <para>
/// The token authorises exactly one session: it carries that session's id and the
/// <see cref="AppRoles.Applicant"/> role only, so a leaked token exposes the journey its holder was
/// already completing and nothing else. This is what lets the public onboarding app stop shipping a
/// shared operator API key to every visitor.
/// </para>
/// </summary>
public sealed class ApplicantSessionTokenService
{
    private readonly SymmetricSecurityKey _signingKey;
    private readonly int _lifetimeMinutes;
    private static readonly JsonWebTokenHandler Handler = new();

    public ApplicantSessionTokenService(IConfiguration configuration)
    {
        _signingKey = new SymmetricSecurityKey(ResolveSigningKey(configuration));

        // Default to the session timeout: the token is useless once the session it names is
        // abandoned, so a shorter life would only strand applicants mid-journey.
        var configured = configuration.GetValue<int?>("Authentication:ApplicantToken:LifetimeMinutes");
        var sessionTimeout = configuration.GetValue<int?>("SessionTimeoutMinutes");
        _lifetimeMinutes = configured is > 0 ? configured.Value
            : sessionTimeout is > 0 ? sessionTimeout.Value
            : 1440;
    }

    public TimeSpan Lifetime => TimeSpan.FromMinutes(_lifetimeMinutes);

    public (string Token, DateTimeOffset ExpiresAt) Issue(Guid sessionId, Guid? customerProfileId)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(Lifetime);

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = sessionId.ToString(),
            [ApplicantSessionAuthenticationDefaults.SessionIdClaim] = sessionId.ToString(),
            [ClaimTypes.Role] = AppRoles.Applicant
        };

        if (customerProfileId is not null)
            claims[ApplicantSessionAuthenticationDefaults.CustomerProfileIdClaim] = customerProfileId.Value.ToString();

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = ApplicantSessionAuthenticationDefaults.Issuer,
            Audience = ApplicantSessionAuthenticationDefaults.Audience,
            Expires = expiresAt.UtcDateTime,
            IssuedAt = DateTime.UtcNow,
            Claims = claims,
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256)
        };

        return (Handler.CreateToken(descriptor), expiresAt);
    }

    public Task<TokenValidationResult> ValidateAsync(string token)
        => Handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = ApplicantSessionAuthenticationDefaults.Issuer,
            ValidAudience = ApplicantSessionAuthenticationDefaults.Audience,
            IssuerSigningKey = _signingKey,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = JwtRegisteredClaimNames.Sub
        });

    /// <summary>
    /// Reports whether a bearer token looks like one this API issued, without validating it.
    /// Used only to route the request to the right authentication scheme; the scheme then
    /// validates properly.
    /// </summary>
    public static bool LooksLikeApplicantToken(string token)
    {
        try
        {
            if (!Handler.CanReadToken(token))
                return false;

            return Handler.ReadJsonWebToken(token).Issuer == ApplicantSessionAuthenticationDefaults.Issuer;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static byte[] ResolveSigningKey(IConfiguration configuration)
    {
        var configured = configuration["Authentication:ApplicantToken:SigningKey"];

        if (!string.IsNullOrWhiteSpace(configured))
            return Encoding.UTF8.GetBytes(configured);

        // Validated at startup for non-Development environments (see ApplicantTokenKeyValidator),
        // so reaching here means development or tests: use an ephemeral key. Tokens then stop
        // working across restarts, which is correct - they are not meant to outlive the process
        // that issued them in that setting.
        return RandomNumberGenerator.GetBytes(64);
    }
}
