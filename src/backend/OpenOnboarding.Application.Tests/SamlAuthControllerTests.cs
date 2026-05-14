using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using OpenOnboarding.Api.Authentication;

namespace OpenOnboarding.Application.Tests;

public sealed class SamlAuthControllerTests
{
    [Fact]
    public async Task Metadata_ReturnsServiceProviderMetadata()
    {
        using var factory = TestWebAppFactory.Create();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/auth/saml/metadata");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/samlmetadata+xml; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        var metadata = await response.Content.ReadAsStringAsync();
        var expectedAcs = $"{client.BaseAddress!.GetLeftPart(UriPartial.Authority)}/auth/saml/callback";
        Assert.Contains("EntityDescriptor", metadata);
        Assert.Contains(expectedAcs, metadata);
    }

    [Fact]
    public async Task Login_IssuesRelayStateCookieAndRedirectsToConfiguredIdp()
    {
        using var factory = TestWebAppFactory.Create();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/auth/saml/login?returnUrl=%2Fadmin%2Fjourney-builder");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Contains("RelayState=", response.Headers.Location!.Query);
        var setCookie = response.Headers.Single(h => h.Key == "Set-Cookie").Value.First();
        Assert.Contains("__Secure-waymark-saml-relay-state", setCookie);
        Assert.Contains("samesite=none", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Callback_WhenAssertionValid_SignsInAndRedirectsToJourneyBuilder()
    {
        using var factory = TestWebAppFactory.Create();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost")
        });

        var loginResponse = await client.GetAsync("/auth/saml/login?returnUrl=%2Fadmin%2Fjourney-builder");
        var relayState = QueryHelpers.ParseQuery(loginResponse.Headers.Location!.Query)["RelayState"].ToString();

        var assertion = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            nameId = "admin@example.com",
            displayName = "Admin User",
            email = "admin@example.com",
            signatureValid = true,
            encrypted = true,
            certificateExpired = false
        })));

        var callbackResponse = await client.PostAsync("/auth/saml/callback", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["SAMLResponse"] = assertion,
            ["RelayState"] = relayState,
            ["returnUrl"] = "/admin/journey-builder"
        }));

        Assert.Equal(HttpStatusCode.Redirect, callbackResponse.StatusCode);
        Assert.Equal("/admin/journey-builder", callbackResponse.Headers.Location?.ToString());
        var setCookies = callbackResponse.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.ToArray()
            : [];
        var adminSessionCookie = Assert.Single(setCookies, value =>
            value.StartsWith($"{AdminSessionAuthenticationDefaults.CookieName}=", StringComparison.Ordinal));
        Assert.Contains("samesite=none", adminSessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", adminSessionCookie, StringComparison.OrdinalIgnoreCase);

        var me = await client.GetFromJsonAsync<AuthMeResponse>("/api/auth/me");
        Assert.NotNull(me);
        Assert.True(me!.Authenticated);
        Assert.Contains("Operator", me.Roles ?? []);
    }

    [Fact]
    public async Task Callback_WhenRelayStateInvalid_RedirectsToLoginWithCsrfError()
    {
        using var factory = TestWebAppFactory.Create();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost")
        });

        await client.GetAsync("/auth/saml/login");

        var assertion = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            nameId = "admin@example.com",
            signatureValid = true,
            encrypted = true,
            certificateExpired = false
        })));

        var callbackResponse = await client.PostAsync("/auth/saml/callback", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["SAMLResponse"] = assertion,
            ["RelayState"] = "invalid",
            ["returnUrl"] = "/admin/journey-builder"
        }));

        Assert.Equal(HttpStatusCode.Redirect, callbackResponse.StatusCode);
        Assert.Equal("/login?error=saml_csrf_failed&returnUrl=%2Fadmin%2Fjourney-builder", callbackResponse.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Callback_WhenAllowedNameIdsNotConfigured_FailsClosed()
    {
        using var factory = TestWebAppFactory.Create(configurationOverrides: new Dictionary<string, string?>
        {
            ["Authentication:Saml:AllowedNameIds:0"] = null
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost")
        });

        var loginResponse = await client.GetAsync("/auth/saml/login");
        var relayState = QueryHelpers.ParseQuery(loginResponse.Headers.Location!.Query)["RelayState"].ToString();
        var assertion = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            nameId = "admin@example.com",
            signatureValid = true,
            encrypted = true,
            certificateExpired = false
        })));

        var callbackResponse = await client.PostAsync("/auth/saml/callback", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["SAMLResponse"] = assertion,
            ["RelayState"] = relayState
        }));

        Assert.Equal(HttpStatusCode.Redirect, callbackResponse.StatusCode);
        Assert.Equal("/login?error=saml_access_denied&returnUrl=%2Fadmin%2Fjourney-builder", callbackResponse.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Login_WhenPlaceholderProviderDisabled_ReturnsNotFound()
    {
        using var factory = TestWebAppFactory.Create(configurationOverrides: new Dictionary<string, string?>
        {
            ["Authentication:Saml:EnablePlaceholderProvider"] = "false"
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/auth/saml/login");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Callback_WhenReturnUrlIsAllowedAbsoluteUrl_RedirectsToFrontendOrigin()
    {
        using var factory = TestWebAppFactory.Create(configurationOverrides: new Dictionary<string, string?>
        {
            ["Authentication:Saml:AllowedReturnOrigins:0"] = "https://frontend.example.test"
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost")
        });

        const string frontendReturnUrl = "https://frontend.example.test/admin/journey-builder";
        var loginResponse = await client.GetAsync($"/auth/saml/login?returnUrl={Uri.EscapeDataString(frontendReturnUrl)}");
        var relayState = QueryHelpers.ParseQuery(loginResponse.Headers.Location!.Query)["RelayState"].ToString();
        var assertion = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            nameId = "admin@example.com",
            signatureValid = true,
            encrypted = true,
            certificateExpired = false
        })));

        var callbackResponse = await client.PostAsync("/auth/saml/callback", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["SAMLResponse"] = assertion,
            ["RelayState"] = relayState,
            ["returnUrl"] = frontendReturnUrl
        }));

        Assert.Equal(HttpStatusCode.Redirect, callbackResponse.StatusCode);
        Assert.Equal(frontendReturnUrl, callbackResponse.Headers.Location?.ToString());
    }

    private sealed class AuthMeResponse
    {
        public bool Authenticated { get; init; }
        public string[]? Roles { get; init; }
    }
}
