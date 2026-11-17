using AMAIS.WKFLO.AI.Application.Extensions;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.ChatStop;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.CORS;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.Extensions;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.Filters;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.Helpers;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.WkfloServer.Hubs;
using AMAIS.WKFLO.Common.Helpers;
using AMAIS.WKFLO.Common.Infrastructure.Extensions;
using AMAIS.WKFLO.Common.Infrastructure.Filters;
using AMAIS.WKFLO.Common.Infrastructure.Middleware;
using AMAIS.WKFLO.Common.MD.Domain.Extensions;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Newtonsoft.Json;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

await AMAIS.WKFLO.AI.WebAPI.Infrastructure.Extensions.ServiceCollectionExtensions.WaitForDaprSidecarWithPollyAsync();

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    options.HandshakeTimeout = TimeSpan.FromSeconds(15);
})
.AddAzureSignalR();

// Register CORS services with Dynamic Policy Provider
builder.Services.AddDynamicCors();

// Consts for authentication schemes
const string DefaultJwtScheme = JwtBearerDefaults.AuthenticationScheme;

// Add services to the container.
builder.Services.AddAuthentication(DefaultJwtScheme) // Esquema por defecto
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"))
    .EnableTokenAcquisitionToCallDownstreamApi()
    .AddInMemoryTokenCaches();

// Configure JWT Bearer for AmaisJwtScheme
var amaisJwtSecurityKey = JwtKeyResolver.Register(builder.Services, builder.Configuration);
var amaisJwtIssuer = builder.Configuration["AmaisJwtScheme:Issuer"];

builder.Services.AddAuthentication()
    .AddJwtBearer(AmaisJwtConst.SchemeName, options =>
    {
        if (amaisJwtSecurityKey != null)
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = amaisJwtIssuer,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = amaisJwtSecurityKey,
                ClockSkew = TimeSpan.FromMinutes(5),
                TryAllIssuerSigningKeys = true
            };
        }

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // Specific logic for your custom token if needed
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                var validatedToken = context.SecurityToken;
                if (validatedToken is JsonWebToken jwt)
                {
                    var rawToken = jwt.EncodedToken;
                    JwtBearerEventsHelper.ApplyAudienceToBasePath(jwt, context.HttpContext.RequestServices);
                }

                return Task.CompletedTask;
            }
        };
    });

// Configure JWT Bearer options for SignalR after Microsoft Identity Web API setup
builder.Services.Configure<JwtBearerOptions>(DefaultJwtScheme, options =>
{
    // Allow trying all keys when kid is missing in the token
    options.TokenValidationParameters.TryAllIssuerSigningKeys = true;

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            // For SignalR, get token from query string
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;

            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/ai/api/v1/chatHub"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddControllers(options =>
{
    options.Filters.Add(typeof(HttpGlobalExceptionFilter));
    options.ModelValidatorProviders.Clear();
})
.AddNewtonsoftJson(options =>
{
    options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
    options.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
    options.SerializerSettings.TypeNameHandling = TypeNameHandling.None;
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
                }
            }
        };

        document.Components.SecuritySchemes[AmaisJwtConst.SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Custom JWT Authorization header using the Bearer scheme."
        };

        return Task.CompletedTask;
    });

    options.AddOperationTransformer((operation, context, cancellationToken) =>
    {
        var endpointMetadata = context.Description.ActionDescriptor.EndpointMetadata;
        var hasAuthorize = endpointMetadata.OfType<AuthorizeAttribute>().Any();

        if (hasAuthorize)
        {
            operation.Responses ??= new OpenApiResponses();
            operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Unauthorized" });
            operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Forbidden" });

            operation.Security ??= new List<OpenApiSecurityRequirement>();
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                // Bound to the document, otherwise Microsoft.OpenApi v2 serializes the requirement as {}.
                [new OpenApiSecuritySchemeReference("oauth2", context.Document)] = new List<string> { $"api://{builder.Configuration["AzureAd:ClientId"]}/access_as_user" }
            });
        }

        // Web chat endpoints authenticate with the AMAIS JWT through a custom filter, not [Authorize].
        var webChatAuthorization = endpointMetadata.OfType<WebChatAuthorizationAttribute>().FirstOrDefault();
        // Skipped when [Authorize] is also present: separate requirement entries would read as "either one is enough".
        if (!hasAuthorize && webChatAuthorization is not null && webChatAuthorization.Method != AuthorizationMethod.DatabaseOnly)
        {
            operation.Responses ??= new OpenApiResponses();
            operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Unauthorized" });

            operation.Security ??= new List<OpenApiSecurityRequirement>();
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(AmaisJwtConst.SchemeName, context.Document)] = new List<string>()
            });

            // With AmaisJwtOrDatabase the token is optional (the agent's own settings can allow the call),
            // so an empty requirement marks the JWT as an alternative rather than mandatory.
            if (webChatAuthorization.Method == AuthorizationMethod.AmaisJwtOrDatabase)
                operation.Security.Add(new OpenApiSecurityRequirement());
        }

        return Task.CompletedTask;
    });

    options.AddOperationTransformer((operation, context, cancellationToken) =>
    {
        var isTenantEndpoint = operation.Tags?.Any(t =>
            t.Name == "Tenant Area" || t.Name == "Tenant API") == true;

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

    options.AddOperationTransformer((operation, context, cancellationToken) =>
    {
        var isAgentChatEndpoint = operation.Tags?.Any(t => t.Name == "Agent Chat") == true;

        if (isAgentChatEndpoint)
        {
            operation.Parameters ??= new List<IOpenApiParameter>();
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = CommonConsts.SubjectId,
                In = ParameterLocation.Header,
                Description = "Anonymous subject identifier (cookie-based)",
                Required = false,
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            });
        }

        return Task.CompletedTask;
    });
});

string serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "amais-wkflo-ai-webapi";
var otelResourceBuilder = OpenTelemetryExtensions.CreateWkfloResourceBuilder(serviceName, builder.Environment);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .SetResourceBuilder(otelResourceBuilder)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            // Custom spans/tags added around the MultiAgentOrchestrator workflow's agent-invocation
            // calls (see InvokeAgentWorkflowActivity/AgentInvoker) — must be registered explicitly or
            // the SDK drops them before they ever reach the OTLP exporter.
            .AddSource("AMAIS.WKFLO.AI.MultiAgentOrchestrator");

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

// Dapr's WorkflowGrpcClient logs a plain "instance not found" lookup at Error (LogGetWorkflowMetadataInstanceNotFound).
// AgentActor.ProcessCandidateAsync checks GetWorkflowStateAsync before scheduling every hand-off, so a miss is the
// normal path and was flooding Grafana with false errors. OTel .NET can't drop a single event without losing the
// resource labels, so the whole category is raised to Critical for the OTel provider only. Failures in that client's
// other calls (schedule, wait, raise event) still throw to our code, where AgentActor logs them itself.
builder.Logging.AddFilter<OpenTelemetryLoggerProvider>("Dapr.Workflow.Client.WorkflowGrpcClient", LogLevel.Critical);

builder.Services.AddDomain();
builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

// US 7501: Stop reaches every replica through a Dapr pub/sub broadcast (pubsub-ai-broadcast, topic wkflo-chat-stop).
builder.Services.AddChatStopBroadcast();

var app = builder.Build();

// Microsoft.Identity.Web wires Microsoft.IdentityModel's own IIdentityLogger pipeline
// (LogHelper.Logger) directly to ILogger, bypassing JwtBearerEvents entirely. That pipeline logs
// IDX10517 ("Signature validation failed. The token's kid is missing.") at Error/"fail" severity
// whenever an AMAIS-issued JWT is also validated (and rejected) by the default Azure AD scheme -
// see IdentityModelLogLevelDowngrader for the full explanation. Wrap the existing logger so that
// specific case is downgraded to Debug while every other message keeps logging unchanged.
LogHelper.Logger = new IdentityModelLogLevelDowngrader(LogHelper.Logger, app.Services.GetRequiredService<ILoggerFactory>());

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

app.UseMiddleware<MultiSchemeAuthenticationMiddleware>();
app.UseMiddleware<UserContextResolver>();
app.UseAutoUserCreation();

app.MapControllers();
app.MapSubscribeHandler();
app.MapActorsHandlers();

// DAPR health check endpoint
app.MapGet("/healthz", () => Results.Ok());

// Map the SignalR hub to match your URL pattern and disable WebSocket transport
app.MapHub<ChatHub>("/api/v1/chathub", options =>
{
    var signalrHttpTransport = Environment.GetEnvironmentVariable("SignalR_HttpTransport");
    if (!string.IsNullOrEmpty(signalrHttpTransport) && signalrHttpTransport.Equals("LongPolling", StringComparison.OrdinalIgnoreCase))
    {
        options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
    }
    else
    {
        options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.ServerSentEvents |
                            Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
    }
});

app.Run();
