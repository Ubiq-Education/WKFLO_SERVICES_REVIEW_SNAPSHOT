using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMAIS.WKFLO.Common.MD.Domain.Data.Config
{
    public class DxpIntegrationConfiguration : IEntityTypeConfiguration<DxpIntegration>
    {
        public void Configure(EntityTypeBuilder<DxpIntegration> builder)
        {
            builder.ToTable("DxpIntegration");

            builder.HasKey(m => m.Id).IsClustered(true);

            builder.HasOne(m => m.Tenant)
                .WithMany()
                .HasForeignKey(m => m.TenantId);

            builder.HasIndex(m => m.TenantId).IsUnique();
        }
    }
}
