namespace OpenOnboarding.Api.Configuration;

public static class ApplicantTokenKeyValidator
{
    /// <summary>Shortest key accepted for HMAC-SHA256; below this the signature is weak.</summary>
    public const int MinimumKeyLength = 32;

    public static void ValidateOrThrow(string? signingKey, string environmentName)
    {
        var isProduction = !string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase);

        if (!isProduction)
        {
            // Development and tests fall back to an ephemeral per-process key.
            return;
        }

        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException(
                "Authentication:ApplicantToken:SigningKey is required in non-Development environments. " +
                "Applicant session tokens are signed with it; without a stable key, tokens stop working " +
                "on restart and across replicas. Store it as a secret.");
        }

        if (signingKey.Length < MinimumKeyLength)
        {
            throw new InvalidOperationException(
                $"Authentication:ApplicantToken:SigningKey must be at least {MinimumKeyLength} characters.");
        }
    }
}
