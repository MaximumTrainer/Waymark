using OpenOnboarding.Api.Configuration;

namespace OpenOnboarding.Application.Tests.Configuration;

public sealed class OnboardingDbConnectionStringValidatorTests
{
    [Fact]
    public void ValidateOrThrow_DoesNotThrow_WhenConnectionStringHasHostAndDatabase()
    {
        var connectionString = "Host=localhost;Port=5432;Database=onboarding;Username=postgres;Password=postgres";

        var ex = Record.Exception(() => OnboardingDbConnectionStringValidator.ValidateOrThrow(connectionString));

        Assert.Null(ex);
    }

    [Fact]
    public void ValidateOrThrow_DoesNotThrow_WhenConnectionStringUsesServerAndInitialCatalog()
    {
        var connectionString = "Server=localhost;Port=5432;Initial Catalog=onboarding;Username=postgres;Password=postgres";

        var ex = Record.Exception(() => OnboardingDbConnectionStringValidator.ValidateOrThrow(connectionString));

        Assert.Null(ex);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateOrThrow_Throws_WhenConnectionStringIsMissing(string? connectionString)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            OnboardingDbConnectionStringValidator.ValidateOrThrow(connectionString));

        Assert.Contains("ConnectionStrings:OnboardingDb", ex.Message);
    }

    [Theory]
    [InlineData("Port=5432;Database=onboarding;Username=postgres;Password=postgres")]
    [InlineData("Host=localhost;Port=5432;Username=postgres;Password=postgres")]
    [InlineData("Host=;Port=5432;Database=onboarding;Username=postgres;Password=postgres")]
    [InlineData("Host=localhost;Port=5432;Database=;Username=postgres;Password=postgres")]
    public void ValidateOrThrow_Throws_WhenConnectionStringMissesHostOrDatabase(string connectionString)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            OnboardingDbConnectionStringValidator.ValidateOrThrow(connectionString));

        Assert.Contains("must include Host and Database", ex.Message);
    }

    [Fact]
    public void ValidateOrThrow_Throws_WhenConnectionStringFormatIsInvalid()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            OnboardingDbConnectionStringValidator.ValidateOrThrow("Host=localhost;Database"));

        Assert.Contains("valid connection string format", ex.Message);
    }
}
