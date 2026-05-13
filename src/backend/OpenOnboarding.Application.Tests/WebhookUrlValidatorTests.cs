using OpenOnboarding.Api.Validation;

namespace OpenOnboarding.Application.Tests;

public sealed class WebhookUrlValidatorTests
{
    [Fact]
    public void IsValidPublicUrl_WithLocalhostUrl_ReturnsFalse()
    {
        Assert.False(WebhookUrlValidator.IsValidPublicUrl("http://localhost/hook"));
    }

    [Fact]
    public void IsValidPublicUrl_With127_0_0_1_ReturnsFalse()
    {
        Assert.False(WebhookUrlValidator.IsValidPublicUrl("http://127.0.0.1/hook"));
    }

    [Fact]
    public void IsValidPublicUrl_WithMetadataServiceUrl_ReturnsFalse()
    {
        Assert.False(WebhookUrlValidator.IsValidPublicUrl("http://169.254.169.254/latest/meta-data"));
    }

    [Fact]
    public void IsValidPublicUrl_WithPrivateRfc1918_ReturnsFalse()
    {
        Assert.False(WebhookUrlValidator.IsValidPublicUrl("http://192.168.1.1/hook"));
    }

    [Fact]
    public void IsValidPublicUrl_WithValidPublicUrl_ReturnsTrue()
    {
        Assert.True(WebhookUrlValidator.IsValidPublicUrl("https://example.com/hook"));
    }

    [Fact]
    public void IsValidPublicUrl_WithNonHttpScheme_ReturnsFalse()
    {
        Assert.False(WebhookUrlValidator.IsValidPublicUrl("ftp://example.com/hook"));
    }

    [Fact]
    public void IsValidPublicUrl_WithAllowPrivateNetworks_True_ReturnsTrue_ForLocalhost()
    {
        Assert.True(WebhookUrlValidator.IsValidPublicUrl("http://localhost/hook", allowPrivateNetworks: true));
    }

    [Fact]
    public void IsValidPublicUrl_WithInvalidUrl_ReturnsFalse()
    {
        Assert.False(WebhookUrlValidator.IsValidPublicUrl("not-a-url"));
    }
}
