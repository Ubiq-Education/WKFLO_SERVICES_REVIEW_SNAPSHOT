using AMAIS.WKFLO.Common.Infrastructure.Extensions;
using AMAIS.WKFLO.Common.MD.Domain.Extensions;
using AMAIS.WKFLO.Worker.Application.Extensions;
using AMAIS.WKFLO.Worker.WebAPI.Infrastructure.Extensions;
using Newtonsoft.Json;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

await AMAIS.WKFLO.Worker.WebAPI.Infrastructure.Extensions.ServiceCollectionExtensions.WaitForDaprSidecarWithPollyAsync();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddNewtonsoftJson(options =>
{
    options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
    options.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
});

builder.Services.AddEndpointsApiExplorer();

string serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "amais-wkflo-worker-webapi";
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
builder.Services.AddWorkerApplication(builder.Configuration);
builder.Services.AddWorkerInfrastructure(builder.Configuration);

var app = builder.Build();

app.MapControllers();
app.MapSubscribeHandler();

app.MapGet("/healthz", () => Results.Ok());

app.Run();
