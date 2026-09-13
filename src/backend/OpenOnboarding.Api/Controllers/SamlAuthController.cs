using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.MvcCore;
using ITfoxtec.Identity.Saml2.Schemas;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using OpenOnboarding.Api.Authentication;
using OpenOnboarding.Api.Authorization;

namespace OpenOnboarding.Api.Controllers;

[ApiController]
[Route("auth/saml")]
[AllowAnonymous]
public sealed class SamlAuthController(
    IConfiguration configuration,
    ILogger<SamlAuthController> logger) : ControllerBase
{
    private const string RelayStateCookie = "__Secure-waymark-saml-relay-state";
    private const string AuthnIdCookie = "__Secure-waymark-saml-authn-id";
    private const string AssertionNamespace = "urn:oasis:names:tc:SAML:2.0:assertion";

    [HttpGet("metadata")]
    [Produces("application/samlmetadata+xml")]
    public IActionResult Metadata()
    {
        var spCert = LoadSpCertificate();
        var issuer = configuration["Authentication:Saml:Issuer"] ?? "waymark-service-provider";
        var acsUrl = ResolveAcsUrl();
        var certBase64 = Convert.ToBase64String(spCert.GetRawCertData());

        var metadataXml = $"""
<?xml version="1.0" encoding="UTF-8"?>
<EntityDescriptor xmlns="urn:oasis:names:tc:SAML:2.0:metadata" entityID="{issuer}">
  <SPSSODescriptor AuthnRequestsSigned="true" WantAssertionsSigned="true" protocolSupportEnumeration="urn:oasis:names:tc:SAML:2.0:protocol">
    <KeyDescriptor use="signing">
      <ds:KeyInfo xmlns:ds="http://www.w3.org/2000/09/xmldsig#">
        <ds:X509Data>
          <ds:X509Certificate>{certBase64}</ds:X509Certificate>
        </ds:X509Data>
      </ds:KeyInfo>
    </KeyDescriptor>
    <KeyDescriptor use="encryption">
      <ds:KeyInfo xmlns:ds="http://www.w3.org/2000/09/xmldsig#">
        <ds:X509Data>
          <ds:X509Certificate>{certBase64}</ds:X509Certificate>
        </ds:X509Data>
      </ds:KeyInfo>
    </KeyDescriptor>
    <AssertionConsumerService Binding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST" Location="{acsUrl}" index="0" isDefault="true" />
  </SPSSODescriptor>
</EntityDescriptor>
""";

        return Content(metadataXml, "application/samlmetadata+xml", Encoding.UTF8);
    }

    [HttpGet("login")]
    public IActionResult Login([FromQuery] string? returnUrl = null)
    {
        var safeReturnUrl = NormalizeReturnUrl(returnUrl);

        var expiredCertificate = FindExpiredCertificate(includeIdpCertificate: false);
        if (expiredCertificate is not null)
        {
            logger.LogError(
                "SAML login refused: the {Role} certificate expired at {NotAfter:u}.",
                expiredCertificate.Value.Role,
                expiredCertificate.Value.NotAfter);
            return Redirect(BuildLoginErrorRedirect("saml_certificate_expired", safeReturnUrl));
        }

        var config = BuildSamlConfiguration();
        var relayState = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var relayStateTimeoutMinutes = GetRelayStateTimeoutMinutes();

        Response.Cookies.Append(RelayStateCookie, relayState, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = "/auth/saml/callback",
            MaxAge = TimeSpan.FromMinutes(relayStateTimeoutMinutes)
        });

        var authnRequest = new Saml2AuthnRequest(config);
        var redirectBinding = new Saml2RedirectBinding();
        redirectBinding.RelayState = relayState;
        redirectBinding.Bind(authnRequest);

        Response.Cookies.Append(AuthnIdCookie, authnRequest.Id.Value, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = "/auth/saml/callback",
            MaxAge = TimeSpan.FromMinutes(relayStateTimeoutMinutes)
        });

        return Redirect(redirectBinding.RedirectLocation.OriginalString);
    }

    [HttpPost("callback")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Callback()
    {
        if (!Request.HasFormContentType)
            return Redirect(BuildLoginErrorRedirect("saml_invalid_assertion", null));

        var form = await Request.ReadFormAsync();
        var safeReturnUrl = NormalizeReturnUrl(form["returnUrl"].ToString());
        var relayState = form["RelayState"].ToString();
        var expectedRelayState = Request.Cookies[RelayStateCookie];
        Response.Cookies.Delete(RelayStateCookie, new CookieOptions { Path = "/auth/saml/callback" });

        if (string.IsNullOrWhiteSpace(relayState) ||
            string.IsNullOrWhiteSpace(expectedRelayState) ||
            !string.Equals(relayState, expectedRelayState, StringComparison.Ordinal))
        {
            return Redirect(BuildLoginErrorRedirect("saml_csrf_failed", safeReturnUrl));
        }

        var expectedAuthnId = Request.Cookies[AuthnIdCookie];
        Response.Cookies.Delete(AuthnIdCookie, new CookieOptions { Path = "/auth/saml/callback" });

        // Without the AuthnRequest ID we cannot prove the response answers a request we issued,
        // so an unsolicited or replayed response must be rejected rather than silently accepted.
        if (string.IsNullOrWhiteSpace(expectedAuthnId))
            return Redirect(BuildLoginErrorRedirect("saml_invalid_assertion", safeReturnUrl));

        var assertionWasEncrypted = ResponseContainsEncryptedAssertion(form["SAMLResponse"].ToString());

        try
        {
            var expiredCertificate = FindExpiredCertificate();
            if (expiredCertificate is not null)
            {
                logger.LogError(
                    "SAML callback rejected: the {Role} certificate expired at {NotAfter:u}.",
                    expiredCertificate.Value.Role,
                    expiredCertificate.Value.NotAfter);
                return Redirect(BuildLoginErrorRedirect("saml_certificate_expired", safeReturnUrl));
            }

            var config = BuildSamlConfiguration();
            var authnResponse = new Saml2AuthnResponse(config);
            var httpRequest = Request.ToGenericHttpRequest(validate: true);
            httpRequest.Binding.ReadSamlResponse(httpRequest, authnResponse);

            if (!string.Equals(authnResponse.InResponseToAsString, expectedAuthnId, StringComparison.Ordinal))
                return Redirect(BuildLoginErrorRedirect("saml_invalid_assertion", safeReturnUrl));

            // The response must be addressed to this SP's assertion consumer service.
            if (!DestinationMatchesAcsUrl(authnResponse.Destination))
                return Redirect(BuildLoginErrorRedirect("saml_invalid_assertion", safeReturnUrl));

            if (authnResponse.Status != Saml2StatusCodes.Success)
                return Redirect(BuildLoginErrorRedirect("saml_invalid_assertion", safeReturnUrl));

            if (RequireEncryptedAssertion() && !assertionWasEncrypted)
            {
                logger.LogWarning(
                    "SAML callback rejected: Authentication:Saml:RequireEncryptedAssertion is enabled "
                    + "but the IdP returned an unencrypted assertion.");
                return Redirect(BuildLoginErrorRedirect("saml_assertion_not_encrypted", safeReturnUrl));
            }

            // Unbind decrypts an EncryptedAssertion and validates the XML signature.
            httpRequest.Binding.Unbind(httpRequest, authnResponse);

            var nameId = authnResponse.NameId?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(nameId))
                return Redirect(BuildLoginErrorRedirect("saml_invalid_assertion", safeReturnUrl));

            var allowedNameIds = configuration
                .GetSection("Authentication:Saml:AllowedNameIds")
                .Get<string[]>()?
                .Select(id => id?.Trim())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToArray() ?? [];

            if (allowedNameIds.Length == 0 ||
                !allowedNameIds.Contains(nameId, StringComparer.OrdinalIgnoreCase))
            {
                return Redirect(BuildLoginErrorRedirect("saml_access_denied", safeReturnUrl));
            }

            var claimsFromAssertion = authnResponse.ClaimsIdentity?.Claims.ToList() ?? [];
            var email = claimsFromAssertion
                .FirstOrDefault(c => c.Type == ClaimTypes.Email || c.Type == "email")?.Value;
            var displayName = claimsFromAssertion
                .FirstOrDefault(c => c.Type == ClaimTypes.GivenName
                                  || c.Type == ClaimTypes.Name
                                  || c.Type == "displayName")?.Value;

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, nameId),
                new(ClaimTypes.Name, displayName?.Trim() ?? nameId),
                new(ClaimTypes.Role, AppRoles.Operator),
                new("auth_provider", "saml")
            };

            if (!string.IsNullOrWhiteSpace(email))
                claims.Add(new Claim(ClaimTypes.Email, email.Trim()));

            var identity = new ClaimsIdentity(claims, AdminSessionAuthenticationDefaults.SchemeName);
            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync(
                AdminSessionAuthenticationDefaults.SchemeName,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = false,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(GetAdminSessionDurationHours())
                });

            return RedirectToValidatedReturnUrl(safeReturnUrl);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (assertionWasEncrypted && IsDecryptionFailure(ex))
            {
                logger.LogError(
                    ex,
                    "SAML callback rejected: the encrypted assertion could not be decrypted with the "
                    + "configured SP private key. Confirm the IdP is encrypting to the certificate "
                    + "published in /auth/saml/metadata.");
            }
            else
            {
                logger.LogError(
                    ex,
                    "SAML callback rejected: {Encryption} assertion failed validation.",
                    assertionWasEncrypted ? "encrypted" : "unencrypted");
            }

            return Redirect(BuildLoginErrorRedirect("saml_invalid_assertion", safeReturnUrl));
        }
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AdminSessionAuthenticationDefaults.SchemeName);
        return NoContent();
    }

    private bool RequireEncryptedAssertion()
        => configuration.GetValue("Authentication:Saml:RequireEncryptedAssertion", false);

    /// <summary>
    /// Reports whether the IdP wrapped the assertion in <c>&lt;saml:EncryptedAssertion&gt;</c>.
    /// Read from the raw response so the answer is available before Unbind decrypts it, and so a
    /// decryption failure can be reported as such rather than as a generic validation failure.
    /// </summary>
    private static bool ResponseContainsEncryptedAssertion(string? encodedResponse)
    {
        if (string.IsNullOrWhiteSpace(encodedResponse))
            return false;

        try
        {
            var xml = Encoding.UTF8.GetString(Convert.FromBase64String(encodedResponse));
            var document = new XmlDocument { XmlResolver = null };
            document.LoadXml(xml);
            return document.GetElementsByTagName("EncryptedAssertion", AssertionNamespace).Count > 0;
        }
        catch (Exception ex) when (ex is FormatException or XmlException or DecoderFallbackException)
        {
            // A malformed response is rejected by Unbind further down; treat it as unencrypted here.
            return false;
        }
    }

    private static bool IsDecryptionFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is CryptographicException)
                return true;

            if (current.Message.Contains("decrypt", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the first configured certificate that has passed its expiry, or <c>null</c> when
    /// all of them are valid. An expired certificate produces an unusable signature or an assertion
    /// that cannot be verified, so it is reported as its own error code rather than folded into a
    /// generic failure. Each endpoint checks only the certificates it actually uses: login signs
    /// with the SP key, while the callback also verifies against the IdP certificate.
    /// </summary>
    private (string Role, DateTime NotAfter)? FindExpiredCertificate(bool includeIdpCertificate = true)
    {
        var now = DateTime.Now;

        var spCert = LoadSpCertificate();
        if (spCert.NotAfter < now)
            return ("service provider", spCert.NotAfter);

        if (!includeIdpCertificate)
            return null;

        var idpCert = LoadIdpCertificate();
        if (idpCert.NotAfter < now)
            return ("identity provider", idpCert.NotAfter);

        return null;
    }

    private Saml2Configuration BuildSamlConfiguration()
    {
        var spCert = LoadSpCertificate();
        var idpCert = LoadIdpCertificate();
        var issuer = configuration["Authentication:Saml:Issuer"] ?? "waymark-service-provider";
        var idpSsoUrl = configuration["Authentication:Saml:IdpSsoUrl"];
        if (string.IsNullOrWhiteSpace(idpSsoUrl))
            throw new InvalidOperationException("Authentication:Saml:IdpSsoUrl must be configured.");

        var config = new Saml2Configuration
        {
            Issuer = issuer,
            SingleSignOnDestination = new Uri(idpSsoUrl),
            SigningCertificate = spCert,
            CertificateValidationMode = System.ServiceModel.Security.X509CertificateValidationMode.None,
            RevocationMode = X509RevocationMode.NoCheck
        };

        config.SignatureValidationCertificates.Add(idpCert);
        // The SP certificate is published in metadata under KeyDescriptor use="encryption", so an
        // IdP may encrypt the assertion to it. Without a decryption certificate the assertion
        // cannot be unwrapped and every such login fails.
        config.DecryptionCertificates.Add(spCert);
        config.AllowedAudienceUris.Add(issuer);

        return config;
    }

    private X509Certificate2 LoadSpCertificate()
    {
        var certPem = configuration["Authentication:Saml:SpCertificate"];
        var keyPem = configuration["Authentication:Saml:SpPrivateKey"];
        if (string.IsNullOrWhiteSpace(certPem) || string.IsNullOrWhiteSpace(keyPem))
            throw new InvalidOperationException(
                "Authentication:Saml:SpCertificate and Authentication:Saml:SpPrivateKey must be configured.");

        return X509Certificate2.CreateFromPem(certPem, keyPem);
    }

    private X509Certificate2 LoadIdpCertificate()
    {
        var certPem = configuration["Authentication:Saml:IdpCertificate"];
        if (string.IsNullOrWhiteSpace(certPem))
            throw new InvalidOperationException("Authentication:Saml:IdpCertificate must be configured.");

        return X509Certificate2.CreateFromPem(certPem);
    }

    private bool DestinationMatchesAcsUrl(Uri? destination)
    {
        // Destination is optional in SAML 2.0, but when the IdP supplies it, it must
        // name our ACS endpoint - otherwise the response was minted for another SP.
        if (destination is null)
            return true;

        return string.Equals(
            destination.OriginalString.TrimEnd('/'),
            ResolveAcsUrl().TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveAcsUrl()
    {
        var configured = configuration["Authentication:Saml:AcsUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        return $"{Request.Scheme}://{Request.Host}/auth/saml/callback";
    }

    private string NormalizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return "/admin/journey-builder";

        if (Uri.TryCreate(returnUrl, UriKind.Relative, out _) &&
            returnUrl.StartsWith("/", StringComparison.Ordinal) &&
            !returnUrl.StartsWith("//", StringComparison.Ordinal))
        {
            return returnUrl;
        }

        if (Uri.TryCreate(returnUrl, UriKind.Absolute, out var absoluteReturnUrl))
        {
            var trustedOrigin = ResolveAllowedAbsoluteReturnOrigin(absoluteReturnUrl);
            if (!string.IsNullOrWhiteSpace(trustedOrigin))
            {
                var pathAndQuery = string.IsNullOrWhiteSpace(absoluteReturnUrl.PathAndQuery)
                    ? "/admin/journey-builder"
                    : absoluteReturnUrl.PathAndQuery;
                return $"{trustedOrigin}{pathAndQuery}{absoluteReturnUrl.Fragment}";
            }
        }

        return "/admin/journey-builder";
    }

    private string? ResolveAllowedAbsoluteReturnOrigin(Uri returnUrl)
    {
        if (!string.Equals(returnUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(returnUrl.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var currentOrigin = $"{Request.Scheme}://{Request.Host}";
        var requestOriginMatches = string.Equals(
            currentOrigin,
            returnUrl.GetLeftPart(UriPartial.Authority),
            StringComparison.OrdinalIgnoreCase);

        if (requestOriginMatches)
            return currentOrigin;

        var allowedReturnOrigins = configuration
            .GetSection("Authentication:Saml:AllowedReturnOrigins")
            .Get<string[]>() ?? [];

        return allowedReturnOrigins.FirstOrDefault(origin =>
            string.Equals(
                origin?.TrimEnd('/'),
                returnUrl.GetLeftPart(UriPartial.Authority),
                StringComparison.OrdinalIgnoreCase));
    }

    private IActionResult RedirectToValidatedReturnUrl(string safeReturnUrl)
    {
        if (Uri.TryCreate(safeReturnUrl, UriKind.Absolute, out _))
            return Redirect(safeReturnUrl);

        return LocalRedirect(safeReturnUrl);
    }

    private static string BuildLoginErrorRedirect(string errorCode, string? returnUrl)
    {
        var query = new Dictionary<string, string?> { ["error"] = errorCode };

        if (!string.IsNullOrWhiteSpace(returnUrl))
            query["returnUrl"] = returnUrl;

        return QueryHelpers.AddQueryString("/login", query);
    }

    private int GetRelayStateTimeoutMinutes()
    {
        var configured = configuration.GetValue<int?>("Authentication:Saml:RelayStateTimeoutMinutes");
        return configured is > 0 ? configured.Value : 5;
    }

    private int GetAdminSessionDurationHours()
    {
        var configured = configuration.GetValue<int?>("Authentication:Saml:SessionDurationHours");
        return configured is > 0 ? configured.Value : 8;
    }
}
