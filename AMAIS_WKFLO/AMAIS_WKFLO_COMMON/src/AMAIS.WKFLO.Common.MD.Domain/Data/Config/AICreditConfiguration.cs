using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMAIS.WKFLO.Common.MD.Domain.Data.Config
{
    public class AICreditConfiguration : IEntityTypeConfiguration<AICredit>
    {
        public void Configure(EntityTypeBuilder<AICredit> builder)
        {
            builder.ToTable("AICredit");

            builder.HasKey(c => c.Id).IsClustered(true);

            builder.HasOne(c => c.AIModel)
                .WithOne(m => m.AICredit)
                .HasForeignKey<AICredit>(c => c.AIModelId);

            // Filtered so a soft-deleted AICredit doesn't block creating a replacement for the same
            // AIModel - the app-level uniqueness validators already exclude deleted rows (!IsDeleted),
            // this must match or the DB constraint would reject what the validators allow.
            builder.HasIndex(c => c.AIModelId)
                .IsUnique()
                .HasDatabaseName("IX_AICredit_AIModelId")
                .HasFilter("[IsDeleted] = 0");

            builder.Property(c => c.CostInMTok).HasPrecision(18, 6);
            builder.Property(c => c.CostOutMTok).HasPrecision(18, 6);
            builder.Property(c => c.MultiplierIn).HasPrecision(9, 4);
            builder.Property(c => c.MultiplierOut).HasPrecision(9, 4);

            // Computed columns (SQL Server) - see AICredit.sql for the persisted formula. Cost is
            // intentionally excluded from Credits; it's already baked into the multipliers. Both are
            // rounded to 2 decimals - these are single admin-facing reference values, not the per-call
            // credits reported to PUM (PumUsageReporter.ComputeCreditsAsync keeps full precision there
            // since those values get summed across many events).
            builder.Property(c => c.TotalCost)
                .HasPrecision(18, 2)
                .HasComputedColumnSql("CAST(ROUND([CostInMTok] + [CostOutMTok], 2) AS DECIMAL(18,2))", stored: true)
                .ValueGeneratedOnAddOrUpdate();

            builder.Property(c => c.Credits)
                .HasPrecision(18, 2)
                .HasComputedColumnSql(
                    "CAST(ROUND((CAST([TokensIn] AS DECIMAL(28,4)) * [MultiplierIn] / 1000) + (CAST([TokensOut] AS DECIMAL(28,4)) * [MultiplierOut] / 1000), 2) AS DECIMAL(18,2))",
                    stored: true)
                .ValueGeneratedOnAddOrUpdate();
        }
    }
}
