using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.TenantSettings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMAIS.WKFLO.Common.MD.Domain.Data.Config
{
    public class TenantSettingConfiguration : IEntityTypeConfiguration<TenantSetting>
    {
        public void Configure(EntityTypeBuilder<TenantSetting> builder)
        {
            builder.ToTable("TenantSetting");

            builder.HasKey(m => m.Id).IsClustered(true);

            builder.Property(m => m.TimeZone)
                .HasMaxLength(100)
                .IsRequired()
                .HasDefaultValue(TenantSettingDefaults.TimeZone);

            builder.Property(m => m.TimeFormat)
                .HasMaxLength(10)
                .IsRequired()
                .HasDefaultValue(TenantSettingDefaults.TimeFormat);

            builder.Property(m => m.DateFormat)
                .HasMaxLength(32)
                .IsRequired()
                .HasDefaultValue(TenantSettingDefaults.DateFormat);

            builder.HasOne(m => m.Tenant)
                .WithMany()
                .HasForeignKey(m => m.TenantId);

            builder.HasIndex(m => m.TenantId).IsUnique();
        }
    }
}
