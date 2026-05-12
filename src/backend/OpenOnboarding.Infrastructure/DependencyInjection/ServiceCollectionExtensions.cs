using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Application.Validators;
using OpenOnboarding.Infrastructure.Persistence;
using OpenOnboarding.Infrastructure.Services;

namespace OpenOnboarding.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("OnboardingDb")
            ?? "Host=localhost;Port=5432;Database=open_onboarding;Username=postgres;Password=postgres";

        services.AddDbContext<OnboardingDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IWorkflowService, WorkflowService>();
        services.AddScoped<IValidator<StartSessionRequest>, StartSessionRequestValidator>();
        services.AddScoped<IValidator<SubmitStepRequest>, SubmitStepRequestValidator>();

        return services;
    }
}
