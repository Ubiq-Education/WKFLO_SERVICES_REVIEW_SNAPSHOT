using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMAIS.WKFLO.Common.MD.Domain.Data.Config
{
    public class AgentConfiguration : IEntityTypeConfiguration<Agent>
    {
        public void Configure(EntityTypeBuilder<Agent> builder)
        {
            builder.ToTable("Agent");

            builder.HasKey(m => m.Id).IsClustered(true);

            builder.HasDiscriminator(b => b.AgentType)
                .HasValue<MarketplaceAgent>(nameof(MarketplaceAgent))
                .HasValue<TenantAgent>(nameof(TenantAgent))
                .HasValue<PublishedTenantAgent>(nameof(PublishedTenantAgent));

            builder.Property(e => e.AgentType)
                .HasMaxLength(100)
                .HasColumnName("AgentType");

            builder.HasOne(m => m.AIModel);
        }
    }
}
