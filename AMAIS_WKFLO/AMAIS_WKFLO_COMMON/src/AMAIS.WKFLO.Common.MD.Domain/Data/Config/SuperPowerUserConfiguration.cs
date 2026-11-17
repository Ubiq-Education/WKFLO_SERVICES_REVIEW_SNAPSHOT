using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMAIS.WKFLO.Common.MD.Domain.Data.Config
{
    public class SuperPowerUserConfiguration : IEntityTypeConfiguration<SuperPowerUser>
    {
        public void Configure(EntityTypeBuilder<SuperPowerUser> builder)
        {
            builder.ToTable("SuperPowerUser");

            builder.HasKey(m => m.Id).IsClustered(true);

            builder.Property(m => m.Email)
                .HasMaxLength(320)
                .IsRequired();

            builder.Property(m => m.CreatedBy)
                .HasMaxLength(320);

            builder.HasIndex(m => m.Email)
                .IsUnique()
                .HasDatabaseName("IX_SuperPowerUser_Email");
        }
    }
}
