using FluentValidation.AspNetCore;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using System.Reflection;
using System.Text.Json.Serialization;
using OpenOnboarding.Api.Authentication;
using OpenOnboarding.Api.Authorization;
using OpenOnboarding.Api.Configuration;
using OpenOnboarding.Application.Exceptions;
using OpenOnboarding.Infrastructure.DependencyInjection;
using OpenOnboarding.Infrastructure.Persistence;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);
OnboardingDbConnectionStringValidator.ValidateOrThrow(
    builder.Configuration.GetConnectionString("OnboardingDb"));
JwtAuthorityValidator.ValidateOrThrow(
    builder.Configuration["Authentication:JwtAuthority"],
    builder.Environment.EnvironmentName);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Open Onboarding API", Version = "v1" });
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    c.IncludeXmlComments(xmlPath);
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Name = "X-Api-Key",
        Type = SecuritySchemeType.ApiKey
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        },
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" }
            },
            Array.Empty<string>()
        }
    });
});

// Authentication: policy scheme that routes to JWT or ApiKey depending on which header is present.
var jwtAuthority = builder.Configuration["Authentication:JwtAuthority"];
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = "Combined";
        options.DefaultChallengeScheme = "Combined";
    })
    .AddPolicyScheme("Combined", "JWT or ApiKey", options =>
    {
        options.ForwardDefaultSelector = context =>
            context.Request.Headers.ContainsKey("X-Api-Key")
                ? ApiKeyAuthenticationHandler.SchemeName
                : context.Request.Cookies.ContainsKey(AdminSessionAuthenticationDefaults.CookieName)
                    ? AdminSessionAuthenticationDefaults.SchemeName
                : JwtBearerDefaults.AuthenticationScheme;
    })
    .AddCookie(AdminSessionAuthenticationDefaults.SchemeName, options =>
    {
        options.Cookie.Name = AdminSessionAuthenticationDefaults.CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
    })
    .AddJwtBearer(options =>
    {
        options.Authority = jwtAuthority;
        options.Audience = builder.Configuration["Authentication:JwtAudience"];
        options.RequireHttpsMetadata = false;
        // When no authority is configured (e.g. development), disable JWT validation.
        if (string.IsNullOrWhiteSpace(jwtAuthority))
        {
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = ctx => { ctx.NoResult(); return Task.CompletedTask; }
            };
        }
    })
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("OperatorOnly", policy => policy.RequireRole(AppRoles.Operator));
    options.AddPolicy("ApplicantOrOperator", policy =>
        policy.RequireRole(AppRoles.Operator, AppRoles.Applicant));
    options.AddPolicy("OperatorOrReadOnly", policy =>
        policy.RequireRole(AppRoles.Operator, AppRoles.ReadOnly));
});
builder.Services.AddScoped<IAuthorizationHandler, SessionOwnershipHandler>();

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddProblemDetails();
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendDev", policy =>
    {
        policy.WithOrigins(
                "http://localhost:5173",
                "http://127.0.0.1:5173",
                "http://localhost:4173",
                "http://127.0.0.1:4173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var environment = builder.Environment.EnvironmentName;
var rateLimitingEnabled = !string.Equals(environment, "Testing", StringComparison.OrdinalIgnoreCase);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = 429,
            Title = "Too Many Requests",
            Detail = "Rate limit exceeded. Please retry after 60 seconds."
        }, cancellationToken);
    };

    if (rateLimitingEnabled)
    {
        var sessionStartLimit = builder.Configuration.GetValue<int>("RateLimiting:SessionStartPerMinute", 100);
        var webhookRegLimit = builder.Configuration.GetValue<int>("RateLimiting:WebhookRegistrationPerMinute", 20);
        var generalLimit = builder.Configuration.GetValue<int>("RateLimiting:GeneralPerMinute", 300);

        options.AddSlidingWindowLimiter("session-start", opt =>
        {
            opt.Window = TimeSpan.FromMinutes(1);
            opt.SegmentsPerWindow = 4;
            opt.PermitLimit = sessionStartLimit;
            opt.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
            opt.QueueLimit = 0;
        });

        options.AddSlidingWindowLimiter("webhook-registration", opt =>
        {
            opt.Window = TimeSpan.FromMinutes(1);
            opt.SegmentsPerWindow = 4;
            opt.PermitLimit = webhookRegLimit;
            opt.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
            opt.QueueLimit = 0;
        });

        options.AddSlidingWindowLimiter("general", opt =>
        {
            opt.Window = TimeSpan.FromMinutes(1);
            opt.SegmentsPerWindow = 4;
            opt.PermitLimit = generalLimit;
            opt.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
            opt.QueueLimit = 0;
        });
    }
    else
    {
        // Testing: add NoLimiter policies so attributes don't error
        options.AddPolicy("session-start", _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("testing"));
        options.AddPolicy("webhook-registration", _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("testing"));
        options.AddPolicy("general", _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("testing"));
    }
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<OnboardingDbContext>("database");

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Open Onboarding API v1");
        c.RoutePrefix = "swagger";
    });
}

// Apply migrations (or create schema when using InMemory for tests) and seed reference data on startup.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OnboardingDbContext>();
    if (db.Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) == true)
        await db.Database.EnsureCreatedAsync();
    else
        await db.Database.MigrateAsync();
    await DataSeeder.SeedAsync(db);
}

app.UseExceptionHandler(exceptionHandler =>
{
    exceptionHandler.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;

        if (exception is ComplianceViolationException cve)
        {
            context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            await context.Response.WriteAsJsonAsync(new
            {
                status = 422,
                title = "Compliance violations",
                violations = cve.Violations.Select(v => new { field = v.Field, message = v.Message, ruleId = v.RuleId })
            });
            return;
        }

        if (exception is ScanFailedException)
        {
            context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            await context.Response.WriteAsJsonAsync(new { error = "File failed security scan" });
            return;
        }

        if (exception is ScanServiceUnavailableException)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = 503,
                Title = "Virus scan service is unavailable.",
            });
            return;
        }

        var (statusCode, title) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Validation failed"),
            InvalidOperationException { Message: var message } when message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                => (StatusCodes.Status404NotFound, "Resource not found"),
            InvalidOperationException => (StatusCodes.Status400BadRequest, "Invalid operation"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error")
        };

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = exception?.Message
        });
    });
});

app.UseHttpsRedirection();
app.UseCors("FrontendDev");
app.UseMiddleware<OpenOnboarding.Api.Middleware.CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Name == "database",
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            components = report.Entries.ToDictionary(
                e => e.Key,
                e => e.Value.Status.ToString())
        });
        await context.Response.WriteAsync(result);
    }
}).AllowAnonymous();

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"status\":\"Healthy\"}");
    }
}).AllowAnonymous();

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            components = report.Entries.ToDictionary(
                e => e.Key,
                e => e.Value.Status.ToString())
        });
        await context.Response.WriteAsync(result);
    }
}).AllowAnonymous();

app.MapControllers();

app.MapMetrics().RequireAuthorization().DisableRateLimiting();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
