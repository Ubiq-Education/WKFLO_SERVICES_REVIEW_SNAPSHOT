using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMAIS.WKFLO.Common.MD.Domain.Data.Config
{
    public class UserStarConfiguration : IEntityTypeConfiguration<UserStar>
    {
        public void Configure(EntityTypeBuilder<UserStar> builder)
        {
            builder.ToTable("UserStar");

            builder.HasKey(m => m.Id).IsClustered(true);

            builder.HasOne(us => us.User)
                   .WithMany()
                   .HasForeignKey(us => us.UserId)
                   .IsRequired(false)
                   .OnDelete(DeleteBehavior.SetNull);

            builder.HasIndex(us => us.UserId)
                   .HasDatabaseName("IX_UserStar_UserId");
        }
    }
}
