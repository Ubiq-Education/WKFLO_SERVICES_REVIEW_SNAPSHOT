using Microsoft.IdentityModel.Abstractions;

namespace AMAIS.WKFLO.AI.WebAPI.Infrastructure.Helpers
{
    /// <summary>
    /// Wraps the <see cref="IIdentityLogger"/> that Microsoft.Identity.Web installs into
    /// <see cref="Microsoft.IdentityModel.Logging.LogHelper.Logger"/> (normally an
    /// <c>IdentityLoggerAdapter</c> forwarding to <c>Microsoft.Extensions.Logging</c>), and
    /// downgrades IDX10517 ("Signature validation failed. The token's kid is missing.") entries
    /// to Debug level.
    /// <para>
    /// ASP.NET Core's <c>UseAuthentication()</c> always runs the default (Azure AD) JWT Bearer
    /// handler, regardless of which scheme an endpoint's authorization attribute actually
    /// requires. So an AMAIS-issued JWT (validated correctly via AmaisJwtScheme, request
    /// succeeds) is also validated against Azure AD's signing keys and fails *signature*
    /// validation there - this is expected noise, not a real failure. That failure happens deep
    /// inside Microsoft.IdentityModel's token validation, before any exception reaches
    /// <c>JwtBearerHandler</c>'s catch block or its <c>OnAuthenticationFailed</c> event, so it is
    /// logged through IdentityModel's own <see cref="IIdentityLogger"/> pipeline
    /// (<c>LogHelper.Logger</c>) at Error/"fail" severity under the
    /// <c>Microsoft.IdentityModel.LoggingExtensions.IdentityLoggerAdapter</c> category -
    /// completely bypassing any <c>ILogger</c>-based event suppression such as
    /// <c>JwtBearerEvents.OnAuthenticationFailed</c>.
    /// </para>
    /// <para>
    /// This wrapper intercepts only IDX10517 entries and reroutes them to Debug via its own
    /// <see cref="ILogger"/>. Every other message - including genuine Azure AD identity/token
    /// errors - is forwarded to the wrapped logger completely unchanged, at its original
    /// severity.
    /// </para>
    /// </summary>
    public sealed class IdentityModelLogLevelDowngrader : IIdentityLogger
    {
        private const string SignatureKeyNotFoundEventId = "IDX10517";

        private readonly IIdentityLogger _inner;
        private readonly ILogger _downgradedLogger;

        public IdentityModelLogLevelDowngrader(IIdentityLogger inner, ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(inner);
            ArgumentNullException.ThrowIfNull(loggerFactory);

            _inner = inner;
            _downgradedLogger = loggerFactory.CreateLogger<IdentityModelLogLevelDowngrader>();
        }

        public bool IsEnabled(EventLogLevel level) => _inner.IsEnabled(level);

        public void Log(LogEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            if (entry.Message is not null &&
                entry.Message.Contains(SignatureKeyNotFoundEventId, StringComparison.Ordinal))
            {
                _downgradedLogger.LogDebug(
                    "Default scheme token signature validation failed (likely a non-Azure-AD token on a multi-scheme endpoint; ignore if the request succeeded via another scheme). {Message}",
                    entry.Message);
                return;
            }

            _inner.Log(entry);
        }
    }
}
