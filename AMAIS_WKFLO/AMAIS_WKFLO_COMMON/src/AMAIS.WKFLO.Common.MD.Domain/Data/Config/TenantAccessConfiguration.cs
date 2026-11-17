using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMAIS.WKFLO.Common.MD.Domain.Data.Config
{
    public class TenantAccessConfiguration : IEntityTypeConfiguration<TenantAccess>
    {
        public void Configure(EntityTypeBuilder<TenantAccess> builder)
        {
            builder.ToTable("TenantAccess");

            builder.HasKey(m => m.Id).IsClustered(true);

            builder.Property(m => m.Email)
                .HasMaxLength(320)
                .IsRequired();

            builder.Property(m => m.CreatedBy)
                .HasMaxLength(320);

            builder.HasOne(m => m.Tenant)
                .WithMany()
                .HasForeignKey(m => m.TenantId)
                .HasConstraintName("FK_TenantAccess_ToTenant");

            builder.HasIndex(m => new { m.TenantId, m.Email })
                .IsUnique()
                .HasDatabaseName("IX_TenantAccess_TenantId_Email");

            builder.HasIndex(m => m.Email)
                .HasDatabaseName("IX_TenantAccess_Email");
        }
    }
}
