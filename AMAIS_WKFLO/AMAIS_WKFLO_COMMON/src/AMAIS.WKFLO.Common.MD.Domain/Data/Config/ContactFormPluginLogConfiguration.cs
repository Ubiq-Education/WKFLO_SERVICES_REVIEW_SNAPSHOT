using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMAIS.WKFLO.Common.MD.Domain.Data.Config
{
    public class ContactFormPluginLogConfiguration : IEntityTypeConfiguration<ContactFormPluginLog>
    {
        public void Configure(EntityTypeBuilder<ContactFormPluginLog> builder)
        {
            builder.ToTable("ContactFormPluginLog");

            builder.HasKey(m => m.Id).IsClustered(true);

            builder.Property(m => m.SubjectId).IsRequired().HasMaxLength(500);
            builder.Property(m => m.SubjectIdSource).IsRequired().HasMaxLength(100);
            builder.Property(m => m.Action).IsRequired().HasMaxLength(100);

            builder.HasOne(m => m.Tenant)
                .WithMany()
                .HasForeignKey(m => m.TenantId);

            builder.HasOne(m => m.Agent)
                .WithMany()
                .HasForeignKey(m => m.AgentId);

            builder.HasIndex(m => new { m.SubjectId, m.TenantId, m.IsDeleted })
                .HasDatabaseName("IX_ContactFormPluginLog_SubjectId_TenantId_IsDeleted");
        }
    }
}
