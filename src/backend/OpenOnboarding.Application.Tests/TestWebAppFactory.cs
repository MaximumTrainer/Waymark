using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenOnboarding.Application.Tests.TestHelpers;
using OpenOnboarding.Infrastructure.Persistence;

namespace OpenOnboarding.Application.Tests;

/// <summary>
/// Shared factory helper for integration-style tests using WebApplicationFactory with InMemory DB.
/// </summary>
internal static class TestWebAppFactory
{
    /// <param name="environment">
    /// Defaults to <c>Testing</c>, which disables rate limiting so unrelated tests are not throttled.
    /// Pass <c>Development</c> to exercise the real limiters.
    /// </param>
    /// <param name="configureServices">
    /// Extra test doubles to register, applied after the DbContext swap so it can replace anything
    /// the application registered - document storage, for instance, which otherwise writes to disk.
    /// </param>
    public static WebApplicationFactory<Program> Create(
        string? dbName = null,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null,
        string environment = "Testing",
        Action<IServiceCollection>? configureServices = null)
    {
        // A shared InMemoryDatabaseRoot ensures all DbContext instances across DI scopes
        // (startup seed scope, test seed scope, request scope) read from the same store.
        var dbRoot = new InMemoryDatabaseRoot();
        var resolvedDbName = dbName ?? Guid.NewGuid().ToString();

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:OnboardingDb"] = "Host=localhost;Database=testdb;",
                    ["Authentication:ApiKey"] = "test-api-key",
                    ["Authentication:JwtAuthority"] = "",
                    ["Authentication:Saml:IdpSsoUrl"] = "https://example-idp.local/sso",
                    ["Authentication:Saml:AllowedNameIds:0"] = "admin@example.com",
                    ["SessionTimeoutMinutes"] = "1440",
                    ["DocumentUpload:MaxFileSizeBytes"] = "10485760"
                };

                if (configurationOverrides is not null)
                {
                    foreach (var overrideSetting in configurationOverrides)
                        settings[overrideSetting.Key] = overrideSetting.Value;
                }

                config.AddInMemoryCollection(settings);
            });
            builder.ConfigureTestServices(services =>
            {
                // Lets a test choose the address a request appears to come from; without it
                // TestServer leaves RemoteIpAddress null and every caller shares one rate limit
                // partition. Inert unless the test sends the header.
                services.AddSingleton<IStartupFilter, ClientAddressStartupFilter>();

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

                configureServices?.Invoke(services);
            });
        });
    }
}
