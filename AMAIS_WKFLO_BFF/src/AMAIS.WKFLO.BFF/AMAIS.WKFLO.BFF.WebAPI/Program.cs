using AMAIS.WKFLO.Common.Infrastructure.Extensions;
using Microsoft.IdentityModel.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

var builder = WebApplication.CreateBuilder(args);

var reverseProxy = builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddRequestTimeouts(options =>
{
    options.AddPolicy("customPolicy", TimeSpan.FromSeconds(20));
});

if (builder.Environment.IsDevelopment())
{
    reverseProxy.ConfigureHttpClient((context, handler) =>
    {
        if (handler is SocketsHttpHandler socketsHttpHandler)
        {
            socketsHttpHandler.SslOptions.RemoteCertificateValidationCallback =
                (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) =>
                {
                    return true;
                };
        }

    });
}

// configure OTel and OTLP
string serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "amais-wkflo-bff-webapi";
var otelResourceBuilder = OpenTelemetryExtensions.CreateWkfloResourceBuilder(serviceName, builder.Environment);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .SetResourceBuilder(otelResourceBuilder)
            .AddSource("Yarp.ReverseProxy")
            .AddAspNetCoreInstrumentation(options =>
            {
                // This creates the SERVER span for YARP
                options.RecordException = true;
            })
            .AddHttpClientInstrumentation(); // outbound from YARP to backend

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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    IdentityModelEventSource.ShowPII = true;
}
else
{
    app.UseHttpsRedirection();
}

// DAPR health check endpoint
app.MapGet("/healthz", () => Results.Ok());

app.UseRequestTimeouts(); // ✅ MUST be before MapReverseProxy

app.MapReverseProxy();

app.Run();
