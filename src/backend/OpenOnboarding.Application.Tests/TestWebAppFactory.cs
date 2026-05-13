using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenOnboarding.Infrastructure.Persistence;

namespace OpenOnboarding.Application.Tests;

/// <summary>
/// Shared factory helper for integration-style tests using WebApplicationFactory with InMemory DB.
/// </summary>
internal static class TestWebAppFactory
{
    public static WebApplicationFactory<Program> Create(string? dbName = null)
    {
        // A shared InMemoryDatabaseRoot ensures all DbContext instances across DI scopes
        // (startup seed scope, test seed scope, request scope) read from the same store.
        var dbRoot = new InMemoryDatabaseRoot();
        var resolvedDbName = dbName ?? Guid.NewGuid().ToString();

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:OnboardingDb"] = "Host=localhost;Database=testdb;",
                    ["Authentication:ApiKey"] = "test-api-key",
                    ["Authentication:JwtAuthority"] = "",
                    ["SessionTimeoutMinutes"] = "1440",
                    ["DocumentUpload:MaxFileSizeBytes"] = "10485760"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                // EF Core 10 uses TryAdd for IDbContextOptionsConfiguration<TContext>, so the Npgsql
                // registration persists unless we explicitly remove it before adding the InMemory one.
                // Remove all DbContext-related registrations for OnboardingDbContext.
                var toRemove = services
                    .Where(d => d.ServiceType == typeof(DbContextOptions<OnboardingDbContext>)
                             || d.ServiceType == typeof(DbContextOptions)
                             || d.ServiceType == typeof(OnboardingDbContext)
                             || (d.ServiceType.IsGenericType
                                 && d.ServiceType.GetGenericTypeDefinition().FullName
                                    == "Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration`1"
                                 && d.ServiceType.GetGenericArguments()[0] == typeof(OnboardingDbContext))
                             || (d.ImplementationType?.Namespace?.Contains("Npgsql") == true))
                    .ToList();
                foreach (var d in toRemove)
                    services.Remove(d);

                services.AddDbContext<OnboardingDbContext>(options =>
                    options.UseInMemoryDatabase(resolvedDbName, dbRoot));
            });
        });
    }
}
