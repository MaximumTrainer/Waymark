namespace OpenOnboarding.Api.Configuration;

public static class OnboardingDbConnectionStringValidator
{
    public static void ValidateOrThrow(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Missing required configuration 'ConnectionStrings:OnboardingDb'. " +
                "Set it in appsettings or via ConnectionStrings__OnboardingDb.");
        }

        if (!connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase) ||
            !connectionString.Contains("Database=", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Invalid 'ConnectionStrings:OnboardingDb' value. " +
                "It must include Host and Database (for example: " +
                "Host=localhost;Port=5432;Database=onboarding;Username=postgres;Password=postgres).");
        }
    }
}
