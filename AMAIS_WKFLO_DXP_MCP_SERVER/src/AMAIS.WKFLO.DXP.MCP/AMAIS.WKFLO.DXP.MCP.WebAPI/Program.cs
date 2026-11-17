using AMAIS.WKFLO.Common.Helpers;
using AMAIS.WKFLO.Common.Infrastructure.Extensions;
using AMAIS.WKFLO.DXP.MCP.Application.Extensions;
using AMAIS.WKFLO.DXP.MCP.Application.Tools;
using AMAIS.WKFLO.DXP.MCP.WebAPI.Infrastructure.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Inbound auth: CMS-issued JWT, validated exactly like the AMAIS MCP Server (AmaisJwtScheme + Key Vault key).
// Audience is not validated because it is the calling CMS site's origin; it is only used to build page URLs.
var jwtSecurityKey = JwtKeyResolver.Register(builder.Services, builder.Configuration);
var jwtIssuer = builder.Configuration["AmaisJwtScheme:Issuer"];

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    if (jwtSecurityKey != null)
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwtSecurityKey,
            ClockSkew = TimeSpan.FromMinutes(5)
        };
    }
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
    .WithTools<DxpPageAnalyticsTools>()
    .AddAuthorizationFilters()
    .WithHttpTransport();

string serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "amais-wkflo-dxp-mcp-webapi";
var otelResourceBuilder = OpenTelemetryExtensions.CreateWkfloResourceBuilder(serviceName, builder.Environment);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(otelResourceBuilder)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .SetResourceBuilder(otelResourceBuilder)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter())
    .WithLogging(logging => logging
        .SetResourceBuilder(otelResourceBuilder)
        .AddOtlpExporter());

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/v1/healthcheck", () => Results.Ok()).AllowAnonymous();

app.MapMcp()
   .RequireAuthorization()
   .RequireRateLimiting("default");

app.Run();
