using OpenOnboarding.Api.Configuration;

namespace OpenOnboarding.Application.Tests.Configuration;

public sealed class JwtAuthorityValidatorTests
{
    [Fact]
    public void Validate_WhenEmpty_InProduction_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            JwtAuthorityValidator.ValidateOrThrow(string.Empty, "Production"));

        Assert.Contains("Authentication:JwtAuthority", ex.Message);
    }

    [Fact]
    public void Validate_WhenEmpty_InDevelopment_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            JwtAuthorityValidator.ValidateOrThrow(string.Empty, "Development"));

        Assert.Null(ex);
    }

    [Fact]
    public void Validate_WhenEmpty_InTesting_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            JwtAuthorityValidator.ValidateOrThrow(string.Empty, "Testing"));

        Assert.Null(ex);
    }

    [Fact]
    public void Validate_WhenProvided_InProduction_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            JwtAuthorityValidator.ValidateOrThrow("https://auth.example.com", "Production"));

        Assert.Null(ex);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WhenNullOrWhitespace_InStaging_ThrowsInvalidOperationException(string? authority)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            JwtAuthorityValidator.ValidateOrThrow(authority, "Staging"));

        Assert.Contains("Authentication:JwtAuthority", ex.Message);
    }
}
