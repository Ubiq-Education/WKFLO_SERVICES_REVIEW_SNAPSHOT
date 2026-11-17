using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMAIS.WKFLO.Common.MD.Domain.Data.Config
{
    public class AgentToolConfiguration : IEntityTypeConfiguration<AgentTool>
    {
        public void Configure(EntityTypeBuilder<AgentTool> builder)
        {
            builder.ToTable("AgentTool");

            builder.HasKey(m => new { m.AgentId, m.ToolId }).IsClustered(true);

            builder.HasOne(m => m.Agent)
                .WithMany(m => m.AgentTools)
                .HasForeignKey(m => m.AgentId);

            builder.HasOne(m => m.Tool)
                .WithMany(m => m.AgentTools)
                .HasForeignKey(m => m.ToolId);
        }
    }
}
