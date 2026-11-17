using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMAIS.WKFLO.Common.MD.Domain.Data.Config
{
    public class ChatImagePurgeStateConfiguration : IEntityTypeConfiguration<ChatImagePurgeState>
    {
        public void Configure(EntityTypeBuilder<ChatImagePurgeState> builder)
        {
            builder.ToTable("ChatImagePurgeState");

            builder.HasKey(m => m.Id).IsClustered(true);

            builder.Property(m => m.LastPurgeLocalDate)
                .HasColumnType("date")
                .IsRequired();

            builder.HasOne(m => m.Tenant)
                .WithMany()
                .HasForeignKey(m => m.TenantId)
                .IsRequired(false);

            builder.HasIndex(m => m.TenantId).IsUnique();
        }
    }
}
