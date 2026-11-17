using AMAIS.WKFLO.Common.Helpers;
using AMAIS.WKFLO.Common.Infrastructure.Extensions;
using AMAIS.WKFLO.Common.Infrastructure.Filters;
using AMAIS.WKFLO.Common.Infrastructure.Middleware;
using AMAIS.WKFLO.Common.Models.Settings;
using AMAIS.WKFLO.MCP.Application.Extensions;
using AMAIS.WKFLO.MCP.Application.Tools;
using AMAIS.WKFLO.MCP.Application.Tools.Amais;
using AMAIS.WKFLO.MCP.Application.Tools.Amais.GalleryFeature;
using AMAIS.WKFLO.MCP.Application.Tools.Amais.Page;
using AMAIS.WKFLO.MCP.Application.Tools.Amais.Post;
using AMAIS.WKFLO.MCP.Application.Tools.Amais.TextFeature;
using AMAIS.WKFLO.MCP.WebAPI.Infrastructure.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options => options.AddPolicy("AllowAnyOrigins",
    builder => builder.AllowAnyOrigin()
        .AllowAnyHeader()
        .AllowAnyMethod()
));

// Configure JWT Bearer
var mcpJwtSecurityKey = JwtKeyResolver.Register(builder.Services, builder.Configuration);
var mcpJwtIssuer = builder.Configuration["AmaisJwtScheme:Issuer"];

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    if (mcpJwtSecurityKey != null)
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = mcpJwtIssuer,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = mcpJwtSecurityKey,
            ClockSkew = TimeSpan.FromMinutes(5)
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
            // Additional logic after token validation
            context.HttpContext.User = context.Principal;
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("default", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
    });
});

builder.Services.AddAuthorization();

builder.Services.AddHttpContextAccessor();
builder.Services.AddMcpServer()
    .WithTools<WeatherTools>()
    .WithTools<AmaisHomeTools>()
    .WithTools<AmaisBlogPostTools>()
    .WithTools<AmaisPageTools>()
    .WithTools<AmaisGalleryFeatureTools>()
    .WithTools<AmaisTextFeatureTools>()
    .AddAuthorizationFilters()
    .WithHttpTransport();

// Configure HttpClientFactory for weather.gov API
builder.Services.AddHttpClient("WeatherApi", client =>
{
    client.BaseAddress = new Uri("https://api.weather.gov");
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("weather-tool", "1.0"));
});

builder.Services.AddHttpClient("AmaisApi")
    .ConfigurePrimaryHttpMessageHandler(() =>
        new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
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


string serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "amais-wkflo-mcp-webapi";
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

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseCors(builder.Configuration.GetSection(nameof(CorsSettings))["PolicyName"]);

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<UserContextResolver>();

app.MapControllers();

app.MapMcp()
   .RequireAuthorization()
   .RequireRateLimiting("default");

app.Run();
