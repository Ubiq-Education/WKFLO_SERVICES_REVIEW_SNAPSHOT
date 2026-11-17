using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMAIS.WKFLO.Common.MD.Domain.Data.Config
{
    public class ToolConfiguration : IEntityTypeConfiguration<Tool>
    {
        public void Configure(EntityTypeBuilder<Tool> builder)
        {
            builder.ToTable("Tool");

            builder.HasKey(m => m.Id).IsClustered(true);

            builder.HasDiscriminator(b => b.ToolType)
                .HasValue<MarketplaceTool>(nameof(MarketplaceTool))
                .HasValue<TenantTool>(nameof(TenantTool))
                .HasValue<PublishedTenantTool>(nameof(PublishedTenantTool));

            builder.Property(e => e.ToolType)
                .HasMaxLength(100)
                .HasColumnName("ToolType");
        }
    }
}
