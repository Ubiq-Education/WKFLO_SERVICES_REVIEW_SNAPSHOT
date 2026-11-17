using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMAIS.WKFLO.Common.MD.Domain.Data.Config
{
    public class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
    {
        public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
        {
            builder.ToTable("AuditLogEntry");
            builder.HasKey(m => m.Id).IsClustered(true);

            builder.Property(m => m.TenantGuid).HasMaxLength(64).IsRequired();
            builder.Property(m => m.EntityType).HasMaxLength(200).IsRequired();
            // 450 rather than a tighter bound: EntityId must comfortably fit the longest audited primary key
            // as-is (e.g. McpOAuthPendingFlow.State, itself HasMaxLength(256) as that entity's own primary
            // key — see McpOAuthPendingFlowConfiguration), plus headroom for composite keys built by
            // MasterDataDbContext.GetEntityId (e.g. EntityTag's "TagId:EntityType:EntityId"). Undersizing
            // this is worse than a merely-missed audit row: AppendAuditLogEntries's try/catch only guards
            // in-memory construction, not the actual INSERT — a value too long for this column would throw
            // out of base.SaveChangesAsync and fail the original CRUD operation it's meant to never block.
            builder.Property(m => m.EntityId).HasMaxLength(450).IsRequired();
            builder.Property(m => m.Action).HasMaxLength(20).IsRequired();
            builder.Property(m => m.ActorUserGuid).HasMaxLength(64);
            builder.Property(m => m.ActorUserName).HasMaxLength(300);
            builder.Property(m => m.ActorUserEmail).HasMaxLength(320);
            builder.Property(m => m.CorrelationId).HasMaxLength(100);
            // Changes is left unconfigured (no HasMaxLength) so EF Core defaults it to an unbounded text
            // column (nvarchar(max) on SQL Server) — using an explicit HasColumnType("nvarchar(max)") here
            // breaks SQLite-backed tests, since SQLite's DDL generator rejects the "(max)" size specifier.

            builder.HasIndex(m => new { m.TenantGuid, m.Timestamp });
            builder.HasIndex(m => new { m.TenantGuid, m.EntityType });
        }
    }
}
