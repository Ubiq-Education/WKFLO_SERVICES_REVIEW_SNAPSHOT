using Microsoft.AspNetCore.Cors.Infrastructure;
using System.Collections.Concurrent;

namespace AMAIS.WKFLO.AI.WebAPI.Infrastructure.CORS;

/// <summary>
/// Dynamic CORS policy provider. The origin check reads the live <see cref="CorsOriginStore"/> snapshot,
/// so policies are built once and reused; the store reloads itself when needed.
/// </summary>
public sealed class DynamicCorsPolicyProvider : ICorsPolicyProvider
{
    private readonly CorsOriginStore _store;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DynamicCorsPolicyProvider> _logger;
    private readonly bool _allowAnyOrigin;
    private readonly ConcurrentDictionary<string, CorsPolicy> _policyCache = new();

    public DynamicCorsPolicyProvider(
        CorsOriginStore store,
        IConfiguration configuration,
        ILogger<DynamicCorsPolicyProvider> logger)
    {
        _store = store;
        _configuration = configuration;
        _logger = logger;
        _allowAnyOrigin = configuration.GetValue<bool>("CorsSettings:AllowAnyOrigin", false);
    }

    public async Task<CorsPolicy> GetPolicyAsync(HttpContext context, string policyName)
    {
        // Use default policy name if none specified
        policyName ??= _configuration.GetSection("CorsSettings:PolicyName").Get<string>() ?? "AllowAnyOrigins";

        if (!_allowAnyOrigin)
        {
            string origin = context.Request.Headers.Origin.ToString();
            await _store.EnsureFreshAsync(origin, context.RequestAborted);
        }

        return _policyCache.GetOrAdd(policyName, _ => CreatePolicy());
    }

    private CorsPolicy CreatePolicy()
    {
        var builder = new CorsPolicyBuilder();
        if (_allowAnyOrigin)
        {
            _logger.LogWarning("CORS configured to allow any origin. This should only be used in development!");
            // A predicate (not AllowAnyOrigin) keeps AllowCredentials valid.
            builder.SetIsOriginAllowed(_ => true);
        }
        else
        {
            builder.SetIsOriginAllowed(_store.IsOriginAllowed);
        }

        return builder
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials() // Required for SignalR
            .Build();
    }
}
