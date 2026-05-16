using Azure.Storage.Blobs;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Contracts.Flows;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Application.Validators;
using OpenOnboarding.Infrastructure.EventBus;
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

        // Register MediatR (handlers are in Infrastructure assembly)
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ServiceCollectionExtensions).Assembly));

        services.AddScoped<IWorkflowService, WorkflowService>();
        services.AddScoped<ISessionAnalyticsService, SessionAnalyticsService>();
        services.AddScoped<IValidator<StartSessionRequest>, StartSessionRequestValidator>();
        services.AddScoped<IValidator<SubmitStepRequest>, SubmitStepRequestValidator>();

        services.AddSingleton<IHostedService, SessionTimeoutService>();

        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IValidator<CreateCustomerRequest>, CreateCustomerRequestValidator>();
        services.AddScoped<IValidator<UpdateCustomerRequest>, UpdateCustomerRequestValidator>();

        services.AddScoped<IFlowService, FlowService>();
        services.AddScoped<IValidator<CreateFlowRequest>, CreateFlowRequestValidator>();
        services.AddScoped<IValidator<UpdateFlowRequest>, UpdateFlowRequestValidator>();

        var storageProvider = configuration.GetValue<string>("DocumentStorage:Provider") ?? "local";
        switch (storageProvider.ToLowerInvariant())
        {
            case "azureblob":
            {
                var blobConnectionString = configuration["DocumentStorage:AzureBlob:ConnectionString"]
                    ?? throw new InvalidOperationException("DocumentStorage:AzureBlob:ConnectionString must be configured when DocumentStorage:Provider is 'azureblob'.");
                var containerName = configuration["DocumentStorage:ContainerName"] ?? "documents";
                var containerClient = new BlobContainerClient(blobConnectionString, containerName);
                services.AddSingleton<IBlobContainerAdapter>(new AzureBlobContainerAdapter(containerClient));
                services.AddSingleton<IDocumentStorageService, BlobDocumentStorageService>();
                break;
            }
            default:
                services.AddScoped<IDocumentStorageService, LocalDocumentStorageService>();
                break;
        }

        if (configuration.GetValue("DocumentStorage:EnableCleanup", true))
            services.AddHostedService<CleanupExpiredDocumentsService>();

        services.AddSingleton<ISessionEventEmitter, InMemorySessionEventEmitter>();

        services.AddHttpClient("Webhook");
        services.AddScoped<IWebhookHttpClient, HttpWebhookClient>();
        services.AddScoped<IWebhookService>(sp => new WebhookService(
            sp.GetRequiredService<OnboardingDbContext>(),
            sp.GetRequiredService<IWebhookHttpClient>(),
            sp.GetRequiredService<IMetricsService>()));

        services.AddSingleton<IMetricsService, PrometheusMetricsService>();

        // Analytics / telemetry: pluggable provider pattern
        if (configuration.GetValue("Analytics:ConsoleProvider:Enabled", true))
            services.AddSingleton<IAnalyticsProvider, ConsoleAnalyticsProvider>();

        services.AddSingleton<ITelemetryService, TelemetryService>();

        // Virus scanning: use real ClamAV adapter when enabled, otherwise no-op
        if (configuration.GetValue<bool>("VirusScan:Enabled"))
            services.AddSingleton<IVirusScanService, ClamAvScanService>();
        else
            services.AddSingleton<IVirusScanService, NullVirusScanService>();

        services.AddScoped<IComplianceRuleEvaluator, ComplianceRuleEvaluator>();

        services.AddHttpClient(nameof(HttpCallbackExecutor));
        services.AddScoped<ILogicNodeExecutor, SetProfileFieldExecutor>();
        services.AddScoped<ILogicNodeExecutor, HttpCallbackExecutor>();
        services.AddScoped<ILogicNodeExecutor, MockVerificationExecutor>();

        // IEventBus: use InMemoryEventBus by default, RabbitMQ if configured
        var eventBusType = configuration.GetValue<string>("EventBus:Type");
        if (eventBusType?.Equals("rabbitmq", StringComparison.OrdinalIgnoreCase) == true)
        {
            var rabbitUri = configuration.GetValue<string>("EventBus:RabbitMq:Uri") ?? "amqp://guest:guest@localhost:5672/";
            var exchange = configuration.GetValue<string>("EventBus:RabbitMq:Exchange") ?? "waymark-events";

            // DeferredEventBus is the IEventBus singleton; the real connection is
            // established asynchronously in RabbitMqEventBusInitializer.StartAsync,
            // removing the previous sync-over-async GetAwaiter().GetResult() call.
            var deferredBus = new DeferredEventBus();
            services.AddSingleton<IEventBus>(deferredBus);
            services.AddSingleton<IHostedService>(sp =>
                new RabbitMqEventBusInitializer(
                    rabbitUri,
                    exchange,
                    deferredBus,
                    sp.GetRequiredService<ILogger<RabbitMqEventBus>>()));
        }
        else
        {
            services.AddScoped<IEventBus, InMemoryEventBus>();
        }

        return services;
    }
}
