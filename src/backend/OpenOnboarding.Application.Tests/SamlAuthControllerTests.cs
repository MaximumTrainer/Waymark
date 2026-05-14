using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;

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
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/auth/saml/login?returnUrl=%2Fadmin%2Fjourney-builder");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Contains("RelayState=", response.Headers.Location!.Query);
        Assert.Contains("__Host-waymark-saml-relay-state", response.Headers.Single(h => h.Key == "Set-Cookie").Value.First());
    }

    [Fact]
    public async Task Callback_WhenAssertionValid_SignsInAndRedirectsToJourneyBuilder()
    {
        using var factory = TestWebAppFactory.Create();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
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
            HandleCookies = true
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
            ["RelayState"] = "invalid"
        }));

        Assert.Equal(HttpStatusCode.Redirect, callbackResponse.StatusCode);
        Assert.Equal("/login?error=saml_csrf_failed", callbackResponse.Headers.Location?.ToString());
    }

    private sealed class AuthMeResponse
    {
        public bool Authenticated { get; init; }
        public string[]? Roles { get; init; }
    }
}
