using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMAIS.WKFLO.Common.MD.Domain.Data.Config
{
    public class AgentAllowedSpinUpAgentConfiguration : IEntityTypeConfiguration<AgentAllowedSpinUpAgent>
    {
        public void Configure(EntityTypeBuilder<AgentAllowedSpinUpAgent> builder)
        {
            builder.ToTable("AgentAllowedSpinUpAgent");

            builder.HasKey(m => new { m.AgentId, m.AllowedSpinUpAgentId }).IsClustered(true);

            // Both FKs reference the same Agent table — EF can't infer two distinct relationships to
            // the same entity by convention, so both need explicit configuration (unlike EntityTag,
            // which only has one real FK).
            builder.HasOne(m => m.Agent)
                .WithMany()
                .HasForeignKey(m => m.AgentId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(m => m.AllowedSpinUpAgent)
                .WithMany()
                .HasForeignKey(m => m.AllowedSpinUpAgentId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
