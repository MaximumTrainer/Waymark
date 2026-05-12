using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace OpenOnboarding.Infrastructure.Persistence;

/// <summary>
/// Enables <c>dotnet ef</c> design-time commands (migrations add, database update, etc.)
/// without needing the full ASP.NET Core host to be running.
/// Run commands from the repo root, e.g.:
///   dotnet ef migrations add MyMigration \
///     --project src/backend/OpenOnboarding.Infrastructure \
///     --startup-project src/backend/OpenOnboarding.Api
/// </summary>
internal sealed class OnboardingDbContextDesignTimeFactory : IDesignTimeDbContextFactory<OnboardingDbContext>
{
    public OnboardingDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("OnboardingDb")
            ?? "Host=localhost;Port=5432;Database=onboarding;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new OnboardingDbContext(options);
    }
}
