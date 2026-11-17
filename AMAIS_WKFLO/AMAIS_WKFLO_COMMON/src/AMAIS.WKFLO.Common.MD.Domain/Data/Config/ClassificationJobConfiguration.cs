using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMAIS.WKFLO.Common.MD.Domain.Data.Config
{
    public class ClassificationJobConfiguration : IEntityTypeConfiguration<ClassificationJob>
    {
        public void Configure(EntityTypeBuilder<ClassificationJob> builder)
        {
            builder.ToTable("ClassificationJob");
            builder.HasKey(m => m.Id).IsClustered(true);

            builder.Property(e => e.JobType).HasMaxLength(100).IsRequired();
            builder.Property(e => e.PreparedAt).IsRequired();
            builder.Property(e => e.QueuedAt);
            builder.Property(e => e.ProcessingStartAt);
            builder.Property(e => e.FinishedAt);
            builder.Property(e => e.IsSuccess);
            builder.Property(e => e.AIModelName).HasMaxLength(300);
            builder.Property(e => e.TotalInputTokens);
            builder.Property(e => e.TotalOutputTokens);
            builder.Property(e => e.ErrorMessage).HasMaxLength(2000);

            builder.HasOne(m => m.Tenant).WithMany().HasForeignKey(m => m.TenantId);
            builder.HasOne(m => m.Agent).WithMany().HasForeignKey(m => m.AgentId);

            builder.HasIndex(m => new { m.TenantId, m.AgentId, m.JobType });
        }
    }
}
