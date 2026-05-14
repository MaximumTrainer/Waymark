using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Infrastructure.DependencyInjection;
using OpenOnboarding.Infrastructure.Services;

namespace OpenOnboarding.Application.Tests;

public sealed class InfrastructureServiceCollectionExtensionsTests
{
    [Fact]
    public void AddInfrastructure_RegistersConsoleAnalyticsProvider_ByDefault()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration();

        services.AddInfrastructure(configuration);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IAnalyticsProvider) &&
            descriptor.ImplementationType == typeof(ConsoleAnalyticsProvider));
    }

    [Fact]
    public void AddInfrastructure_DoesNotRegisterConsoleAnalyticsProvider_WhenDisabled()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Analytics:ConsoleProvider:Enabled"] = "false"
        });

        services.AddInfrastructure(configuration);

        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IAnalyticsProvider) &&
            descriptor.ImplementationType == typeof(ConsoleAnalyticsProvider));
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:OnboardingDb"] = "Host=localhost;Database=open_onboarding;Username=postgres;Password=postgres",
            ["VirusScan:Enabled"] = "false"
        };

        if (overrides is not null)
        {
            foreach (var entry in overrides)
            {
                values[entry.Key] = entry.Value;
            }
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
