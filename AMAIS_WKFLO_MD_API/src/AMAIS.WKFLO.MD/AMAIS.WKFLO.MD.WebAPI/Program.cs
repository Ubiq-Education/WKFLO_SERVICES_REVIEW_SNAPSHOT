using AMAIS.WKFLO.Common.Infrastructure.Extensions;
using AMAIS.WKFLO.Common.Infrastructure.Filters;
using AMAIS.WKFLO.Common.Infrastructure.Middleware;
using AMAIS.WKFLO.Common.MD.Domain.Extensions;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Settings;
using AMAIS.WKFLO.MD.Application.Extensions;
using AMAIS.WKFLO.MD.WebAPI.Infrastructure.Extensions;
using AMAIS.WKFLO.MD.WebAPI.Infrastructure.OpenApi.Transformers.Examples;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Logging;
using Microsoft.OpenApi;
using Newtonsoft.Json;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

await AMAIS.WKFLO.MD.WebAPI.Infrastructure.Extensions.ServiceCollectionExtensions.WaitForDaprSidecarWithPollyAsync();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options => options.AddPolicy("AllowAnyOrigins",
    builder => builder.AllowAnyOrigin()
        .AllowAnyHeader()
        .AllowAnyMethod()
));

// Add services to the container.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

// Add authorization with policies that support both user scopes and app roles
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.ApiAccess, policy =>
    {
        policy.Requirements.Add(new ScopeOrAppRoleRequirement(
            acceptedScopes: new[] { Scopes.AccessAsUser },
            acceptedRoles: new[] { AppRoles.ApiAccess }
        ));
    });
});

// Register the custom authorization handler
builder.Services.AddSingleton<IAuthorizationHandler, ScopeOrAppRoleAuthorizationHandler>();

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(typeof(HttpGlobalExceptionFilter));
    options.Filters.Add(new AuthorizeFilter(AuthorizationPolicies.ApiAccess));
    options.ModelValidatorProviders.Clear();
})
.AddNewtonsoftJson(options =>
{
    options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
    options.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
    options.SerializerSettings.TypeNameHandling = TypeNameHandling.None;
});

builder.Services.Configure<RouteOptions>(options =>
{
    options.LowercaseUrls = true;
    options.LowercaseQueryStrings = true;
});

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Components ??= new();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes["oauth2"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Flows = new OpenApiOAuthFlows
            {
                AuthorizationCode = new OpenApiOAuthFlow
                {
                    AuthorizationUrl = new Uri($"{builder.Configuration["AzureAd:Instance"]}{builder.Configuration["AzureAd:TenantId"]}/oauth2/v2.0/authorize"),
                    TokenUrl = new Uri($"{builder.Configuration["AzureAd:Instance"]}{builder.Configuration["AzureAd:TenantId"]}/oauth2/v2.0/token"),
                    Scopes = new Dictionary<string, string>
                    {
                        { $"api://{builder.Configuration["AzureAd:ClientId"]}/access_as_user", "Access the API as a user" }
                    }
                },
                ClientCredentials = new OpenApiOAuthFlow
                {
                    TokenUrl = new Uri($"{builder.Configuration["AzureAd:Instance"]}{builder.Configuration["AzureAd:TenantId"]}/oauth2/v2.0/token"),
                    Scopes = new Dictionary<string, string>
                    {
                        { $"api://{builder.Configuration["AzureAd:ClientId"]}/.default", "Access the API as an application" }
                    }
                }
            },
            Description = "Use OAuth2 for user authentication (Implicit Flow) or Client Credentials for machine-to-machine authentication"
        };

        return Task.CompletedTask;
    });

    options.AddOperationTransformer((operation, context, cancellationToken) =>
    {
        var hasAuthorize = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<AuthorizeAttribute>().Any();

        if (hasAuthorize)
        {
            operation.Responses ??= new OpenApiResponses();
            operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Unauthorized" });
            operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Forbidden" });

            // The reference must be bound to the document, otherwise Microsoft.OpenApi v2 serializes it as {}.
            // Two alternatives: delegated users (access_as_user) or client credentials (.default), as ApiAccess accepts both.
            operation.Security ??= new List<OpenApiSecurityRequirement>();
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("oauth2", context.Document)] = new List<string> { $"api://{builder.Configuration["AzureAd:ClientId"]}/access_as_user" }
            });
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("oauth2", context.Document)] = new List<string> { $"api://{builder.Configuration["AzureAd:ClientId"]}/.default" }
            });
        }

        return Task.CompletedTask;
    });

    options.AddOperationTransformer((operation, context, cancellationToken) =>
    {
        var isTenantEndpoint = operation.Tags?.Any(t => t.Name == "Tenant Area") == true;

        if (isTenantEndpoint)
        {
            operation.Parameters ??= new List<IOpenApiParameter>();
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = CommonConsts.TenantName,
                In = ParameterLocation.Header,
                Description = "Tenant Name",
                Required = false,
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Default = "Ubiq"
                }
            });
        }

        return Task.CompletedTask;
    });

    options.AddOperationTransformer<CreateAIModelExampleTransformer>();
    options.AddOperationTransformer<UpdateAIModelExampleTransformer>();
    options.AddOperationTransformer<CreateTenantAgentExampleTransformer>();
    options.AddOperationTransformer<UpdateTenantAgentExampleTransformer>();
    options.AddOperationTransformer<UpdateTenantAgentToolPermissionsExampleTransformer>();
    options.AddOperationTransformer<CreateMarketplaceAgentExampleTransformer>();
    options.AddOperationTransformer<UpdateMarketplaceAgentExampleTransformer>();
    options.AddOperationTransformer<CreateTenantAgentChatExampleTransformer>();
    options.AddOperationTransformer<CreateAgentChatSettingExampleTransformer>();
    options.AddOperationTransformer<UpdateAgentChatSettingExampleTransformer>();
    options.AddOperationTransformer<CreateSecretExampleTransformer>();
    options.AddOperationTransformer<UpdateSecretExampleTransformer>();
    options.AddOperationTransformer<CreateTenantExampleTransformer>();
    options.AddOperationTransformer<CreateTenantToolExampleTransformer>();
    options.AddOperationTransformer<UpdateTenantToolExampleTransformer>();
    options.AddOperationTransformer<UpdateTenantToolFunctionsExampleTransformer>();
    options.AddOperationTransformer<CreateMarketplaceToolExampleTransformer>();
    options.AddOperationTransformer<UpdateMarketplaceToolExampleTransformer>();
});

string serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "amais-wkflo-md-webapi";
var otelResourceBuilder = OpenTelemetryExtensions.CreateWkfloResourceBuilder(serviceName, builder.Environment);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .SetResourceBuilder(otelResourceBuilder)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        tracing
            //.AddConsoleExporter()
            .AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics
            .SetResourceBuilder(otelResourceBuilder)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        metrics
            //.AddConsoleExporter()
            .AddOtlpExporter();
    })
    .WithLogging(logging =>
    {
        logging.SetResourceBuilder(otelResourceBuilder)
            .AddOtlpExporter();
    });

builder.Services.AddDomain();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseCors(builder.Configuration.GetSection(nameof(CorsSettings))["PolicyName"]);

app.UseMiddleware<TenantContextResolver>();
app.UseMiddleware<ChatContextResolver>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    IdentityModelEventSource.ShowPII = true;
}

var openApiEnabled = app.Environment.IsDevelopment() ||
    string.Equals(Environment.GetEnvironmentVariable("SCALAR_ENABLED"), "true", StringComparison.OrdinalIgnoreCase);

if (openApiEnabled)
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options
        .AddOAuth2Authentication("oauth2", scheme => scheme
            .WithFlows(flows => flows
                .WithAuthorizationCode(flow => flow
                    .WithClientId(builder.Configuration["AzureAd:ClientId"]!)
                    .WithSelectedScopes([$"api://{builder.Configuration["AzureAd:ClientId"]}/access_as_user"])
                )
            )
        )
    );
}

app.UseAuthentication();

app.UseAuthorization();

app.UseMiddleware<UserContextResolver>();
app.UseAutoUserCreation();
app.UseMiddleware<AuditActorContextResolver>();

app.MapControllers();
app.MapSubscribeHandler();
app.MapActorsHandlers();

app.MapControllerRoute(
    name: "chat",
    pattern: "chat/{id}",
    defaults: new { controller = "Chat", action = "Index" });

// DAPR health check endpoint
app.MapGet("/healthz", () => Results.Ok());

app.Run();
