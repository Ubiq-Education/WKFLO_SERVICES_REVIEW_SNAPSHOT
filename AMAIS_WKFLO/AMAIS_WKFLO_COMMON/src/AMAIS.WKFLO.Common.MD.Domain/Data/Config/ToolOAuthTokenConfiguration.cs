using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMAIS.WKFLO.Common.MD.Domain.Data.Config
{
    public class ToolOAuthTokenConfiguration : IEntityTypeConfiguration<ToolOAuthToken>
    {
        public void Configure(EntityTypeBuilder<ToolOAuthToken> builder)
        {
            builder.ToTable("ToolOAuthToken");

            builder.HasKey(m => m.Id).IsClustered(true);

            // At most one live token record per MCP OAuth connection. Deliberately no FK to Tool: a
            // TenantTool and its PublishedTenantTool share one connection id (and therefore one row here),
            // so this can't be a single-owner relationship the way Tool.Id would imply.
            //
            // Two filtered unique indexes, not one composite unique index on (McpOAuthConnectionId, UserId):
            // SQL Server treats NULLs as distinct by default, so a plain composite unique index would allow
            // any number of shared (UserId IS NULL) rows for the same connection — it only stops duplicate
            // per-user rows.
            //
            // What these two indexes enforce, precisely: at most one shared row per connection, and at most
            // one row per (connection, user) pair — each independently. They do NOT prevent a connection from
            // having both a shared row and one or more per-user rows at once (e.g. left over after a tool's
            // OAuthScope is switched Shared -> PerUser or back) — that would need a database-level mechanism
            // this schema doesn't have (a check constraint can't reference other rows; would need a trigger).
            // Shared-vs-per-user mode is fixed per tool (see McpServerConfig.OAuthScope), but "never mixed" as
            // an operational guarantee is enforced at the application layer instead: McpOAuthScopeResolver
            // never resolves a PerUser tool's requesting-user id to null (it throws
            // McpOAuthPerUserIdentityUnavailableException rather than falling back to null — see its remarks),
            // so a PerUser tool's operations can never accidentally read/delete a stray Shared row even if one
            // exists for the same connection id.
            builder.HasIndex(m => m.McpOAuthConnectionId)
                .IsUnique()
                .HasDatabaseName("IX_ToolOAuthToken_McpOAuthConnectionId")
                .HasFilter("[UserId] IS NULL");

            builder.HasIndex(m => new { m.McpOAuthConnectionId, m.UserId })
                .IsUnique()
                .HasDatabaseName("IX_ToolOAuthToken_McpOAuthConnectionId_UserId")
                .HasFilter("[UserId] IS NOT NULL");
        }
    }
}
