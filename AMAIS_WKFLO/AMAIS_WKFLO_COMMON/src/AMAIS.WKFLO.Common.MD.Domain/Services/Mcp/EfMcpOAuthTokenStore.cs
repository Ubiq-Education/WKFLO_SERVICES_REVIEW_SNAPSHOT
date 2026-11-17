using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.Common.MD.Domain.Services.Mcp
{
    /// <summary>
    /// EF-backed <see cref="IMcpOAuthTokenStore"/>, keyed directly by <see cref="ToolOAuthToken.McpOAuthConnectionId"/>.
    /// </summary>
    public class EfMcpOAuthTokenStore : IMcpOAuthTokenStore
    {
        // Matches ToolOAuthToken.LastError's NVARCHAR(500) column. Longer messages are truncated rather
        // than passed through as-is: an untruncated write can fail the SaveChangesAsync call outright,
        // which — since callers treat recording a connection result as best-effort — would silently leave
        // the status row reporting a stale result instead of the real (if verbose) failure reason.
        private const int MaxLastErrorLength = 500;

        private readonly IMasterDataDbContext _db;
        private readonly IMcpTokenProtector _protector;
        private readonly IDateTimeProvider _dateTimeProvider;

        public EfMcpOAuthTokenStore(IMasterDataDbContext db, IMcpTokenProtector protector, IDateTimeProvider dateTimeProvider)
        {
            _db = db;
            _protector = protector;
            _dateTimeProvider = dateTimeProvider;
        }

        public async Task<McpOAuthTokenData> GetAsync(Guid connectionId, Guid? userId = null, CancellationToken cancellationToken = default)
        {
            var entity = await _db.ToolOAuthTokens
                .Where(m => m.McpOAuthConnectionId == connectionId && m.UserId == userId)
                .FirstOrDefaultAsync(cancellationToken);

            return entity == null ? null : ToData(entity, _protector);
        }

        public async Task SaveAsync(Guid connectionId, McpOAuthTokenData token, Guid? userId = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(token);

            var entity = await _db.ToolOAuthTokens
                .Where(m => m.McpOAuthConnectionId == connectionId && m.UserId == userId)
                .FirstOrDefaultAsync(cancellationToken);

            if (entity == null)
            {
                entity = new ToolOAuthToken
                {
                    McpOAuthConnectionId = connectionId,
                    UserId = userId,
                    ConnectedOnUtc = token.ObtainedAt
                };
                await _db.ToolOAuthTokens.AddAsync(entity, cancellationToken);
            }

            entity.AccessToken = _protector.Protect(token.AccessToken);
            entity.RefreshToken = _protector.Protect(token.RefreshToken);
            entity.TokenType = token.TokenType;
            entity.TokenObtainedAtUtc = token.ObtainedAt;
            entity.ExpiresAtUtc = token.ExpiresIn.HasValue ? token.ObtainedAt.Add(token.ExpiresIn.Value) : null;
            entity.Scope = token.Scope;
            entity.ClientId = token.ClientId;
            entity.ClientSecret = _protector.Protect(token.ClientSecret);
            entity.TokenEndpointAuthMethod = token.TokenEndpointAuthMethod;
            entity.AuthorizationServer = token.AuthorizationServer;

            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task DeleteAsync(Guid connectionId, Guid? userId = null, CancellationToken cancellationToken = default)
        {
            var entity = await _db.ToolOAuthTokens
                .Where(m => m.McpOAuthConnectionId == connectionId && m.UserId == userId)
                .FirstOrDefaultAsync(cancellationToken);

            if (entity == null)
                return;

            _db.ToolOAuthTokens.Remove(entity);
            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task<bool> ExistsAsync(Guid connectionId, Guid? userId = null, CancellationToken cancellationToken = default)
        {
            return await _db.ToolOAuthTokens
                .Where(m => m.McpOAuthConnectionId == connectionId && m.UserId == userId)
                .AnyAsync(cancellationToken);
        }

        public async Task RecordConnectionResultAsync(Guid connectionId, bool succeeded, string error, Guid? userId = null, CancellationToken cancellationToken = default)
        {
            var entity = await _db.ToolOAuthTokens
                .Where(m => m.McpOAuthConnectionId == connectionId && m.UserId == userId)
                .FirstOrDefaultAsync(cancellationToken);

            // No token row yet — there's nothing to annotate, and the caller has nothing to report against
            // (the status endpoint already reports "not connected" for a missing row).
            if (entity == null)
                return;

            entity.LastCheckedOnUtc = _dateTimeProvider.DateTimeOffsetUtcNow();
            entity.LastCheckSucceeded = succeeded;
            entity.LastError = succeeded ? null : Truncate(error, MaxLastErrorLength);

            await _db.SaveChangesAsync(cancellationToken);
        }

        private static string Truncate(string value, int maxLength) =>
            value != null && value.Length > maxLength ? value[..maxLength] : value;

        private static McpOAuthTokenData ToData(ToolOAuthToken entity, IMcpTokenProtector protector) => new McpOAuthTokenData
        {
            TokenType = entity.TokenType,
            AccessToken = protector.Unprotect(entity.AccessToken),
            RefreshToken = protector.Unprotect(entity.RefreshToken),
            ExpiresIn = entity.ExpiresAtUtc.HasValue ? entity.ExpiresAtUtc.Value - entity.TokenObtainedAtUtc : null,
            Scope = entity.Scope,
            ObtainedAt = entity.TokenObtainedAtUtc,
            ClientId = entity.ClientId,
            ClientSecret = protector.Unprotect(entity.ClientSecret),
            TokenEndpointAuthMethod = entity.TokenEndpointAuthMethod,
            AuthorizationServer = entity.AuthorizationServer
        };
    }
}
