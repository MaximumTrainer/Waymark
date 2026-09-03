using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.MvcCore;
using ITfoxtec.Identity.Saml2.Schemas;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens.Saml2;
using OpenOnboarding.Api.Authentication;

namespace OpenOnboarding.Application.Tests;

public sealed class SamlAuthControllerTests
{
    // Generate one cert pair for the entire test class; reuse across tests for speed.
    private static readonly TestCertPair SharedCerts = TestCertPair.Generate();

    // -----------------------------------------------------------------------
    // Helper: generate a self-signed RSA cert pair for test use
    // -----------------------------------------------------------------------
    private sealed class TestCertPair
    {
        public string PemCert { get; private init; } = "";
        public string PemKey { get; private init; } = "";

        public static TestCertPair Generate()
        {
            using var rsa = RSA.Create(2048);
            var req = new CertificateRequest(
                "CN=test-saml", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            // Use a temp cert to export the key PEM; CreateSelfSigned disposes the key on some platforms
            var cert = req.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddYears(-1),
                DateTimeOffset.UtcNow.AddYears(10));

            return new TestCertPair
            {
                PemCert = cert.ExportCertificatePem(),
                PemKey  = rsa.ExportPkcs8PrivateKeyPem()
            };
        }
    }

    // -----------------------------------------------------------------------
    // Helper: build the config overrides needed for every SAML test
    // -----------------------------------------------------------------------
    private static Dictionary<string, string?> SamlConfig(
        TestCertPair? certs = null,
        string nameId = "admin@example.com")
    {
        var c = certs ?? SharedCerts;
        return new Dictionary<string, string?>
        {
            ["Authentication:Saml:Issuer"]          = "test-sp",
            ["Authentication:Saml:IdpSsoUrl"]       = "https://test-idp.local/sso",
            ["Authentication:Saml:SpCertificate"]   = c.PemCert,
            ["Authentication:Saml:SpPrivateKey"]    = c.PemKey,
            // Same cert plays the IdP role in tests
            ["Authentication:Saml:IdpCertificate"]  = c.PemCert,
            ["Authentication:Saml:AllowedNameIds:0"] = nameId
        };
    }

    // -----------------------------------------------------------------------
    // Helper: build the IdP-side Saml2Configuration for generating test responses
    // -----------------------------------------------------------------------
    private static Saml2Configuration IdpConfig(TestCertPair certs)
    {
        var cfg = new Saml2Configuration
        {
            Issuer             = "test-idp",
            SignatureAlgorithm = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256",
            SigningCertificate = X509Certificate2.CreateFromPem(certs.PemCert, certs.PemKey),
            CertificateValidationMode =
                System.ServiceModel.Security.X509CertificateValidationMode.None,
            RevocationMode = X509RevocationMode.NoCheck
        };
        cfg.AllowedAudienceUris.Add("test-sp");
        return cfg;
    }

    // -----------------------------------------------------------------------
    // Helper: generate a base64-encoded SAMLResponse form value
    // -----------------------------------------------------------------------
    private static string BuildSamlResponse(
        Saml2Configuration idpCfg,
        string inResponseTo,
        string nameId           = "admin@example.com",
        string acsUrl           = "https://localhost/auth/saml/callback",
        Saml2StatusCodes status  = Saml2StatusCodes.Success)
    {
        var authnResponse = new Saml2AuthnResponse(idpCfg)
        {
            Status      = status,
            Destination = new Uri(acsUrl),
            ClaimsIdentity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, nameId),
                new Claim(ClaimTypes.Email,          nameId)
            })
        };
        authnResponse.NameId             = new Saml2NameIdentifier(nameId, NameIdentifierFormats.Email);
        authnResponse.InResponseToAsString = inResponseTo;

        if (status == Saml2StatusCodes.Success)
            authnResponse.CreateSecurityToken(idpCfg.AllowedAudienceUris.FirstOrDefault() ?? "test-sp");

        var binding = new Saml2PostBinding();
        binding.Bind(authnResponse);

        var match = Regex.Match(
            binding.PostContent,
            @"name=""SAMLResponse""\s+value=""([^""]+)""",
            RegexOptions.IgnoreCase);

        if (!match.Success)
            throw new InvalidOperationException(
                "Could not extract SAMLResponse value from ITfoxtec HTML form output.");

        return match.Groups[1].Value;
    }

    // -----------------------------------------------------------------------
    // Helper: extract a named cookie value from a response's Set-Cookie headers
    // -----------------------------------------------------------------------
    private static string? ExtractCookieValue(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
            return null;

        foreach (var cookie in cookies)
        {
            var pair = cookie.Split(';')[0].Trim();
            if (pair.StartsWith(name + "=", StringComparison.Ordinal))
                return pair[(name.Length + 1)..];
        }

        return null;
    }

    // -----------------------------------------------------------------------
    // Helper: POST a SAMLResponse to the callback endpoint with manual cookies
    // -----------------------------------------------------------------------
    private static Task<HttpResponseMessage> PostCallbackAsync(
        HttpClient client,
        string samlResponse,
        string relayState,
        string? relayStateCookie,
        string? authnIdCookie,
        string? returnUrl = null)
    {
        var form = new Dictionary<string, string>
        {
            ["SAMLResponse"] = samlResponse,
            ["RelayState"]   = relayState
        };

        if (returnUrl is not null)
            form["returnUrl"] = returnUrl;

        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/saml/callback")
        {
            Content = new FormUrlEncodedContent(form)
        };

        var cookies = new List<string>();
        if (relayStateCookie is not null)
            cookies.Add($"__Secure-waymark-saml-relay-state={relayStateCookie}");
        if (authnIdCookie is not null)
            cookies.Add($"__Secure-waymark-saml-authn-id={authnIdCookie}");

        if (cookies.Count > 0)
            request.Headers.Add("Cookie", string.Join("; ", cookies));

        return client.SendAsync(request);
    }

    // -----------------------------------------------------------------------
    // Helper: create an HttpClient that does NOT automatically follow redirects
    //         and does NOT absorb Set-Cookie headers.
    // -----------------------------------------------------------------------
    private static HttpClient RawClient(WebApplicationFactory<Program> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies     = false,
            BaseAddress       = new Uri("https://localhost")
        });

    // -----------------------------------------------------------------------
    // Helper: perform the login step and return (relayState, authnId, client)
    // -----------------------------------------------------------------------
    private static async Task<(string relayState, string authnId, string relayStateCookieRaw, string authnIdCookieRaw)>
        DoLoginAsync(HttpClient client)
    {
        var loginResp = await client.GetAsync("/auth/saml/login?returnUrl=%2Fadmin%2Fjourney-builder");
        Assert.Equal(HttpStatusCode.Redirect, loginResp.StatusCode);

        var location   = loginResp.Headers.Location!;
        var relayState = QueryHelpers.ParseQuery(location.Query)["RelayState"].ToString();
        var authnId    = ExtractCookieValue(loginResp, "__Secure-waymark-saml-authn-id")
                         ?? throw new InvalidOperationException("authn-id cookie missing");
        var rsRaw      = ExtractCookieValue(loginResp, "__Secure-waymark-saml-relay-state")
                         ?? throw new InvalidOperationException("relay-state cookie missing");

        return (relayState, authnId, rsRaw, authnId);
    }

    // =======================================================================
    // 1. Happy path: login → callback → session
    // =======================================================================
    [Fact]
    public async Task FullLoginFlow_ValidAssertion_SignsInAndReturnsOperatorRole()
    {
        using var factory = TestWebAppFactory.Create(configurationOverrides: SamlConfig());
        using var client  = RawClient(factory);

        // Step 1: login
        var (relayState, authnId, rsCookieRaw, authnIdCookieRaw) = await DoLoginAsync(client);

        // Step 2: build a real SAML response
        var samlResp = BuildSamlResponse(IdpConfig(SharedCerts), authnId);

        // Step 3: POST to callback with manual cookies
        var callbackResp = await PostCallbackAsync(
            client, samlResp, relayState, rsCookieRaw, authnIdCookieRaw);
        Assert.Equal(HttpStatusCode.Redirect, callbackResp.StatusCode);
        Assert.Equal("/admin/journey-builder", callbackResp.Headers.Location?.ToString());

        var sessionCookie = ExtractCookieValue(callbackResp, AdminSessionAuthenticationDefaults.CookieName);
        Assert.NotNull(sessionCookie);

        // Step 4: verify /api/auth/me
        var meReq = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meReq.Headers.Add("Cookie",
            $"{AdminSessionAuthenticationDefaults.CookieName}={sessionCookie}");
        var meResp = await client.SendAsync(meReq);
        var me     = await meResp.Content.ReadFromJsonAsync<AuthMeResponse>();

        Assert.NotNull(me);
        Assert.True(me!.Authenticated);
        Assert.Contains("Operator", me.Roles ?? []);
    }

    // =======================================================================
    // 2. Metadata includes SP X.509 certificate
    // =======================================================================
    [Fact]
    public async Task Metadata_IncludesSpCertificateInKeyDescriptor()
    {
        using var factory = TestWebAppFactory.Create(configurationOverrides: SamlConfig());
        using var client  = factory.CreateClient();

        var resp = await client.GetAsync("/auth/saml/metadata");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/samlmetadata+xml; charset=utf-8",
            resp.Content.Headers.ContentType?.ToString());

        var xml = await resp.Content.ReadAsStringAsync();
        Assert.Contains("EntityDescriptor", xml);
        Assert.Contains("X509Certificate", xml);
        Assert.Contains("KeyDescriptor", xml);
    }

    // =======================================================================
    // 3. Login redirects with a real deflate-compressed SAMLRequest
    // =======================================================================
    [Fact]
    public async Task Login_RedirectsToIdp_WithSamlRequestParameter()
    {
        using var factory = TestWebAppFactory.Create(configurationOverrides: SamlConfig());
        using var client  = RawClient(factory);

        var resp = await client.GetAsync("/auth/saml/login");

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        var location = resp.Headers.Location!;
        var qs       = QueryHelpers.ParseQuery(location.Query);

        // Real SAML redirect binding: SAMLRequest, RelayState; NOT a JSON blob
        Assert.True(qs.ContainsKey("SAMLRequest"), "SAMLRequest query param expected");
        Assert.True(qs.ContainsKey("RelayState"),  "RelayState query param expected");

        // Verify it's deflate-base64 XML, not JSON
        var raw   = Convert.FromBase64String(qs["SAMLRequest"].ToString());
        using var ms  = new MemoryStream();
        using var def = new System.IO.Compression.DeflateStream(
            new MemoryStream(raw), System.IO.Compression.CompressionMode.Decompress);
        def.CopyTo(ms);
        var xml = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("AuthnRequest", xml);
        Assert.DoesNotContain("\"issuer\"", xml); // must not be JSON

        // Cookies must be set
        Assert.NotNull(ExtractCookieValue(resp, "__Secure-waymark-saml-relay-state"));
        Assert.NotNull(ExtractCookieValue(resp, "__Secure-waymark-saml-authn-id"));
    }

    // =======================================================================
    // 4. Tampered signature → error
    // =======================================================================
    [Fact]
    public async Task Callback_TamperedSignature_RedirectsToSamlInvalidAssertion()
    {
        using var factory = TestWebAppFactory.Create(configurationOverrides: SamlConfig());
        using var client  = RawClient(factory);

        var (relayState, authnId, rsCookieRaw, authnIdCookieRaw) = await DoLoginAsync(client);

        // Build a valid response then tamper
        var samlResp = BuildSamlResponse(IdpConfig(SharedCerts), authnId);
        var xml = Encoding.UTF8.GetString(Convert.FromBase64String(samlResp));
        xml = xml.Replace("admin@example.com", "attacker@evil.example");
        var tampered = Convert.ToBase64String(Encoding.UTF8.GetBytes(xml));

        var resp = await PostCallbackAsync(
            client, tampered, relayState, rsCookieRaw, authnIdCookieRaw);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("error=saml_invalid_assertion",
            resp.Headers.Location?.ToString() ?? "");
    }

    // =======================================================================
    // 5. InResponseTo mismatch → error
    // =======================================================================
    [Fact]
    public async Task Callback_InResponseToMismatch_RedirectsToSamlInvalidAssertion()
    {
        using var factory = TestWebAppFactory.Create(configurationOverrides: SamlConfig());
        using var client  = RawClient(factory);

        var (relayState, authnId, rsCookieRaw, authnIdCookieRaw) = await DoLoginAsync(client);

        // Build response with the wrong InResponseTo
        var samlResp = BuildSamlResponse(IdpConfig(SharedCerts), "_wrong_id_value");

        var resp = await PostCallbackAsync(
            client, samlResp, relayState, rsCookieRaw, authnIdCookieRaw);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("error=saml_invalid_assertion",
            resp.Headers.Location?.ToString() ?? "");
    }

    // =======================================================================
    // 6. Expired assertion (tampered NotOnOrAfter) → error
    // =======================================================================
    [Fact]
    public async Task Callback_ExpiredAssertion_RedirectsToSamlInvalidAssertion()
    {
        using var factory = TestWebAppFactory.Create(configurationOverrides: SamlConfig());
        using var client  = RawClient(factory);

        var (relayState, authnId, rsCookieRaw, authnIdCookieRaw) = await DoLoginAsync(client);

        var samlResp = BuildSamlResponse(IdpConfig(SharedCerts), authnId);
        var xml = Encoding.UTF8.GetString(Convert.FromBase64String(samlResp));
        // Push all validity timestamps into the past; signature becomes invalid too,
        // but either way the callback must reject with saml_invalid_assertion.
        xml = Regex.Replace(xml, @"NotOnOrAfter=""[^""]*""",
            $@"NotOnOrAfter=""{DateTimeOffset.UtcNow.AddHours(-2):yyyy-MM-ddTHH:mm:ssZ}""");
        var expired = Convert.ToBase64String(Encoding.UTF8.GetBytes(xml));

        var resp = await PostCallbackAsync(
            client, expired, relayState, rsCookieRaw, authnIdCookieRaw);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("error=saml_invalid_assertion",
            resp.Headers.Location?.ToString() ?? "");
    }

    // =======================================================================
    // 7. CSRF: relay state mismatch → saml_csrf_failed
    // =======================================================================
    [Fact]
    public async Task Callback_RelayStateMismatch_RedirectsToCsrfError()
    {
        using var factory = TestWebAppFactory.Create(configurationOverrides: SamlConfig());
        using var client  = RawClient(factory);

        var (_, authnId, rsCookieRaw, authnIdCookieRaw) = await DoLoginAsync(client);

        var samlResp = BuildSamlResponse(IdpConfig(SharedCerts), authnId);

        var resp = await PostCallbackAsync(
            client, samlResp, relayState: "WRONG_RELAY_STATE", // intentionally wrong
            rsCookieRaw, authnIdCookieRaw);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("error=saml_csrf_failed",
            resp.Headers.Location?.ToString() ?? "");
    }

    // =======================================================================
    // 8. NameId not in AllowedNameIds → saml_access_denied
    // =======================================================================
    [Fact]
    public async Task Callback_NameIdNotAllowed_RedirectsToAccessDenied()
    {
        // Allowed list is only "admin@example.com"; assertion says "other@example.com"
        using var factory = TestWebAppFactory.Create(configurationOverrides: SamlConfig());
        using var client  = RawClient(factory);

        var (relayState, authnId, rsCookieRaw, authnIdCookieRaw) = await DoLoginAsync(client);

        // Build response for a different nameId not in the allowlist
        var samlResp = BuildSamlResponse(
            IdpConfig(SharedCerts), authnId, nameId: "other@example.com");

        var resp = await PostCallbackAsync(
            client, samlResp, relayState, rsCookieRaw, authnIdCookieRaw);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("error=saml_access_denied",
            resp.Headers.Location?.ToString() ?? "");
    }

    // =======================================================================
    // Callback: valid absolute return URL in allowed origins → preserved
    // =======================================================================
    [Fact]
    public async Task Callback_AllowedAbsoluteReturnUrl_RedirectsToFrontendOrigin()
    {
        var overrides = SamlConfig();
        overrides["Authentication:Saml:AllowedReturnOrigins:0"] = "https://frontend.example.test";

        using var factory = TestWebAppFactory.Create(configurationOverrides: overrides);
        using var client  = RawClient(factory);

        const string frontendReturn = "https://frontend.example.test/admin/journey-builder";
        var loginResp = await client.GetAsync(
            $"/auth/saml/login?returnUrl={Uri.EscapeDataString(frontendReturn)}");
        Assert.Equal(HttpStatusCode.Redirect, loginResp.StatusCode);

        var relayState     = QueryHelpers.ParseQuery(loginResp.Headers.Location!.Query)["RelayState"].ToString();
        var authnId        = ExtractCookieValue(loginResp, "__Secure-waymark-saml-authn-id")!;
        var rsCookieRaw    = ExtractCookieValue(loginResp, "__Secure-waymark-saml-relay-state")!;

        var samlResp = BuildSamlResponse(IdpConfig(SharedCerts), authnId);

        var resp = await PostCallbackAsync(
            client, samlResp, relayState, rsCookieRaw, authnId, frontendReturn);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal(frontendReturn, resp.Headers.Location?.ToString());
    }

    // =======================================================================
    // 9. Unsigned response (signature stripped) -> saml_invalid_assertion
    // =======================================================================
    [Fact]
    public async Task Callback_UnsignedResponse_RedirectsToSamlInvalidAssertion()
    {
        using var factory = TestWebAppFactory.Create(configurationOverrides: SamlConfig());
        using var client  = RawClient(factory);

        var (relayState, authnId, rsCookieRaw, authnIdCookieRaw) = await DoLoginAsync(client);

        var samlResp = BuildSamlResponse(IdpConfig(SharedCerts), authnId);
        var xml = Encoding.UTF8.GetString(Convert.FromBase64String(samlResp));
        // Remove every ds:Signature element so the response arrives entirely unsigned.
        xml = Regex.Replace(
            xml, @"<(\w+:)?Signature\b.*?</(\w+:)?Signature>", "", RegexOptions.Singleline);
        Assert.DoesNotContain("Signature", xml);
        var unsigned = Convert.ToBase64String(Encoding.UTF8.GetBytes(xml));

        var resp = await PostCallbackAsync(client, unsigned, relayState, rsCookieRaw, authnIdCookieRaw);

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("error=saml_invalid_assertion", resp.Headers.Location?.ToString() ?? "");
    }

    // =======================================================================
    // 10. Missing authn-id cookie -> InResponseTo cannot be verified -> error
    // =======================================================================
    [Fact]
    public async Task Callback_MissingAuthnIdCookie_RedirectsToSamlInvalidAssertion()
    {
        using var factory = TestWebAppFactory.Create(configurationOverrides: SamlConfig());
        using var client  = RawClient(factory);

        var (relayState, authnId, rsCookieRaw, _) = await DoLoginAsync(client);

        var samlResp = BuildSamlResponse(IdpConfig(SharedCerts), authnId);

        var resp = await PostCallbackAsync(
            client, samlResp, relayState, rsCookieRaw, authnIdCookie: null);

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("error=saml_invalid_assertion", resp.Headers.Location?.ToString() ?? "");
    }

    // =======================================================================
    // 11. Destination (recipient ACS URL) mismatch -> saml_invalid_assertion
    // =======================================================================
    [Fact]
    public async Task Callback_DestinationMismatch_RedirectsToSamlInvalidAssertion()
    {
        using var factory = TestWebAppFactory.Create(configurationOverrides: SamlConfig());
        using var client  = RawClient(factory);

        var (relayState, authnId, rsCookieRaw, authnIdCookieRaw) = await DoLoginAsync(client);

        // Correctly signed, but addressed to a different service provider ACS endpoint.
        var samlResp = BuildSamlResponse(
            IdpConfig(SharedCerts), authnId, acsUrl: "https://evil.example/auth/saml/callback");

        var resp = await PostCallbackAsync(client, samlResp, relayState, rsCookieRaw, authnIdCookieRaw);

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("error=saml_invalid_assertion", resp.Headers.Location?.ToString() ?? "");
    }

    private sealed class AuthMeResponse
    {
        public bool Authenticated { get; init; }
        public string[]? Roles { get; init; }
    }
}
