using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMAIS.WKFLO.Common.MD.Domain.Data.Config
{
    public class McpOAuthPendingFlowConfiguration : IEntityTypeConfiguration<McpOAuthPendingFlow>
    {
        public void Configure(EntityTypeBuilder<McpOAuthPendingFlow> builder)
        {
            builder.ToTable("McpOAuthPendingFlow");

            // Keyed by the OAuth `state` value itself, not a surrogate id — see the entity's remarks.
            builder.HasKey(m => m.State).IsClustered(true);

            builder.Property(m => m.State).HasMaxLength(256);
            builder.Property(m => m.ClientId).HasMaxLength(500);
            builder.Property(m => m.TokenEndpointAuthMethod).HasMaxLength(100);
            builder.Property(m => m.TokenEndpoint).HasMaxLength(1000);
            builder.Property(m => m.AuthorizationServerIssuer).HasMaxLength(1000);
            builder.Property(m => m.Resource).HasMaxLength(1000);
            builder.Property(m => m.Scope).HasMaxLength(500);
            builder.Property(m => m.RedirectUri).HasMaxLength(1000);

            // BeginAsync's opportunistic cleanup deletes everything past ExpiresOnUtc in one bounded
            // DELETE ... WHERE ExpiresOnUtc < now; and CompleteAsync's lookup filters on it too. Neither
            // needs a dedicated composite index — this table only ever holds a handful of in-flight rows
            // (each deleted within minutes, one way or another) so a full scan of ExpiresOnUtc is cheap, but
            // an index still keeps both queries index-only rather than scanning the whole (tiny) table.
            builder.HasIndex(m => m.ExpiresOnUtc)
                .HasDatabaseName("IX_McpOAuthPendingFlow_ExpiresOnUtc");
        }
    }
}
