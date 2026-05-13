using FluentValidation.AspNetCore;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
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

var builder = WebApplication.CreateBuilder(args);
OnboardingDbConnectionStringValidator.ValidateOrThrow(
    builder.Configuration.GetConnectionString("OnboardingDb"));

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
                : JwtBearerDefaults.AuthenticationScheme;
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

// Apply EF Core migrations and seed reference data on startup.
// Runs in all environments (including Testing / CI) so the schema is always current.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OnboardingDbContext>();
    await db.Database.MigrateAsync();
    await DataSeeder.SeedAsync(db);
}

app.UseExceptionHandler(exceptionHandler =>
{
    exceptionHandler.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
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
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
