using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMAIS.WKFLO.Common.MD.Domain.Data.Config
{
    public class UserInstallConfiguration : IEntityTypeConfiguration<UserInstall>
    {
        public void Configure(EntityTypeBuilder<UserInstall> builder)
        {
            builder.ToTable("UserInstall");

            builder.HasKey(m => m.Id).IsClustered(true);

            builder.HasOne(ui => ui.User)
                   .WithMany()
                   .HasForeignKey(ui => ui.UserId)
                   .IsRequired(false)
                   .OnDelete(DeleteBehavior.SetNull);

            builder.HasIndex(ui => ui.UserId)
                   .HasDatabaseName("IX_UserInstall_UserId");
        }
    }
}
