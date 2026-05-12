using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Contracts.Flows;
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
            ?? throw new InvalidOperationException("Connection string 'OnboardingDb' must be configured.");

        services.AddDbContext<OnboardingDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IWorkflowService, WorkflowService>();
        services.AddScoped<ISessionAnalyticsService, SessionAnalyticsService>();
        services.AddScoped<IValidator<StartSessionRequest>, StartSessionRequestValidator>();
        services.AddScoped<IValidator<SubmitStepRequest>, SubmitStepRequestValidator>();

        services.AddHostedService<SessionTimeoutService>();

        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IValidator<CreateCustomerRequest>, CreateCustomerRequestValidator>();
        services.AddScoped<IValidator<UpdateCustomerRequest>, UpdateCustomerRequestValidator>();

        services.AddScoped<IFlowService, FlowService>();
        services.AddScoped<IValidator<CreateFlowRequest>, CreateFlowRequestValidator>();
        services.AddScoped<IValidator<UpdateFlowRequest>, UpdateFlowRequestValidator>();

        return services;
    }
}
