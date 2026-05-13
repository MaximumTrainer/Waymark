namespace OpenOnboarding.Api.Configuration;

public static class JwtAuthorityValidator
{
    public static void ValidateOrThrow(string? jwtAuthority, string environmentName)
    {
        var isProduction = !string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase);
        if (isProduction && string.IsNullOrWhiteSpace(jwtAuthority))
        {
            throw new InvalidOperationException(
                "Authentication:JwtAuthority is required in non-Development environments. " +
                "Set this configuration value to your JWT issuer URL.");
        }
    }
}
