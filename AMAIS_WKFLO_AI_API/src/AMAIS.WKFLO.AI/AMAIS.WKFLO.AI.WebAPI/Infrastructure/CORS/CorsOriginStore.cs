using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Settings;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace AMAIS.WKFLO.AI.WebAPI.Infrastructure.CORS;

/// <summary>
/// Singleton, in-memory snapshot of the allowed CORS origins (active SignalR web origins of all tenants).
/// <list type="bullet">
/// <item>The first load, and a request with an unknown origin (throttled), wait for a reload, so newly added domains work immediately.</item>
/// <item>An expired TTL never blocks a request from an already-allowed origin: the reload runs in the background.</item>
/// </list>
/// </summary>
public sealed class CorsOriginStore
{
    public const string LocalhostFallback = "http://localhost";
    private const string SignalROriginType = "SignalR";
    private static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultMissRefreshThrottle = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<CorsOriginStore> _logger;
    private readonly TimeSpan _refreshInterval;
    private readonly TimeSpan _missRefreshThrottle;
    private readonly object _refreshGate = new();

    private volatile HashSet<string> _origins = new(StringComparer.OrdinalIgnoreCase);
    // UTC ticks of the last load attempt (successful or not). 0 means never loaded. Use Volatile.Read/Write.
    private long _lastAttemptTicks;
    // The load in flight, shared by every caller (guarded by _refreshGate). It never faults.
    private Task _inflightLoad = Task.CompletedTask;
    private int _lastLoggedCount = -1;

    public CorsOriginStore(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        TimeProvider timeProvider,
        IHostApplicationLifetime lifetime,
        ILogger<CorsOriginStore> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _lifetime = lifetime;
        _logger = logger;

        var settings = configuration.GetSection("CorsSettings").Get<CorsSettings>();
        _refreshInterval = settings?.OriginsRefreshSeconds > 0
            ? TimeSpan.FromSeconds(settings.OriginsRefreshSeconds.Value)
            : DefaultRefreshInterval;
        _missRefreshThrottle = settings?.OriginsMissRefreshSeconds > 0
            ? TimeSpan.FromSeconds(settings.OriginsMissRefreshSeconds.Value)
            : DefaultMissRefreshThrottle;
    }

    /// <summary>
    /// True when the origin is in the current snapshot. An empty snapshot allows localhost only.
    /// </summary>
    public bool IsOriginAllowed(string origin)
    {
        var normalized = Normalize(origin);
        if (normalized.Length == 0)
            return false;

        var snapshot = _origins;
        return snapshot.Count == 0
            ? string.Equals(normalized, LocalhostFallback, StringComparison.OrdinalIgnoreCase)
            : snapshot.Contains(normalized);
    }

    /// <summary>
    /// Makes sure the snapshot is usable for the given request origin.
    /// The cancellation token only limits how long this caller waits for a reload, it never cancels the shared load.
    /// </summary>
    public async Task EnsureFreshAsync(string origin, CancellationToken cancellationToken)
    {
        var observedTicks = Volatile.Read(ref _lastAttemptTicks);
        if (observedTicks == 0)
        {
            await RefreshAsync(observedTicks, cancellationToken);
            return;
        }

        var age = _timeProvider.GetUtcNow() - new DateTimeOffset(observedTicks, TimeSpan.Zero);
        var unknownOrigin = !string.IsNullOrWhiteSpace(origin) && !IsOriginAllowed(origin);

        if (unknownOrigin && age >= _missRefreshThrottle)
            await RefreshAsync(observedTicks, cancellationToken);
        else if (age >= _refreshInterval)
            StartBackgroundRefresh(observedTicks);
    }

    private void StartBackgroundRefresh(long observedTicks) => _ = GetOrStartLoad(observedTicks);

    /// <summary>
    /// Waits for the shared load with this caller's own token. Cancelling it only stops this caller
    /// from waiting (including the caller that started the load); the load itself carries on for everyone else.
    /// </summary>
    private Task RefreshAsync(long observedTicks, CancellationToken waitToken)
        => GetOrStartLoad(observedTicks).WaitAsync(waitToken);

    /// <summary>
    /// Single flight: joins the load already running, or starts one unless another caller
    /// already refreshed after <paramref name="observedTicks"/> was read.
    /// </summary>
    private Task GetOrStartLoad(long observedTicks)
    {
        lock (_refreshGate)
        {
            if (!_inflightLoad.IsCompleted)
                return _inflightLoad;

            if (Volatile.Read(ref _lastAttemptTicks) != observedTicks)
                return Task.CompletedTask;

            _inflightLoad = Task.Run(LoadAndPublishAsync);
            return _inflightLoad;
        }
    }

    /// <summary>Never throws: failures are logged and the previous snapshot is kept.</summary>
    private async Task LoadAndPublishAsync()
    {
        HashSet<string> snapshot = null;
        try
        {
            // Independent of any request: ApplicationStopping plus a short timeout.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ApplicationStopping);
            cts.CancelAfter(LoadTimeout);
            snapshot = await LoadAsync(cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reload CORS allowed origins. Keeping the previous snapshot ({Count} origins).", _origins.Count);
        }

        // Recorded first, and also on failure, so an unavailable database is not hit on every request.
        Volatile.Write(ref _lastAttemptTicks, _timeProvider.GetUtcNow().UtcTicks);

        if (snapshot is not null)
            Publish(snapshot);
    }

    private async Task<HashSet<string>> LoadAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IMasterDataDbContext>();
        var names = await db.WebOrigins
            .AsNoTracking()
            .Where(w => w.IsActive && !w.IsDeleted && w.WebOriginType == SignalROriginType)
            .Select(w => w.Name)
            .ToListAsync(cancellationToken);

        var snapshot = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var normalized = Normalize(name);
            if (normalized.Length > 0)
                snapshot.Add(normalized);
        }

        return snapshot;
    }

    private void Publish(HashSet<string> snapshot)
    {
        _origins = snapshot;

        if (snapshot.Count != _lastLoggedCount)
        {
            _lastLoggedCount = snapshot.Count;
            _logger.LogInformation("CORS allowed origins loaded: {Count} origins", snapshot.Count);
        }

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("CORS allowed origins: {Origins}", string.Join(", ", snapshot));
    }

    /// <summary>
    /// Trims, drops a trailing slash and, like <c>CorsPolicyBuilder.WithOrigins</c>, converts a Unicode host to punycode.
    /// </summary>
    private static string Normalize(string origin)
    {
        var trimmed = origin?.Trim().TrimEnd('/') ?? string.Empty;
        if (trimmed.Length == 0 || Ascii.IsValid(trimmed))
            return trimmed;

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.IdnHost))
        {
            var port = uri.IsDefaultPort ? string.Empty : ":" + uri.Port;
            return $"{uri.Scheme}://{uri.IdnHost}{port}";
        }

        return trimmed;
    }
}
