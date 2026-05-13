using System.Data.Common;

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

        DbConnectionStringBuilder builder = new();
        try
        {
            builder.ConnectionString = connectionString;
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                "Invalid 'ConnectionStrings:OnboardingDb' value. " +
                "It must be a valid connection string format.", ex);
        }

        if (!TryGetNonEmptyValue(builder, "Host", "Server") ||
            !TryGetNonEmptyValue(builder, "Database", "Initial Catalog"))
        {
            throw new InvalidOperationException(
                "Invalid 'ConnectionStrings:OnboardingDb' value. " +
                "It must include Host and Database (for example: " +
                "Host=localhost;Port=5432;Database=onboarding;Username=postgres;Password=postgres).");
        }
    }

    private static bool TryGetNonEmptyValue(DbConnectionStringBuilder builder, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (builder.TryGetValue(key, out var value) &&
                value is string stringValue &&
                !string.IsNullOrWhiteSpace(stringValue))
            {
                return true;
            }
        }

        return false;
    }
}
