using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMAIS.WKFLO.Common.MD.Domain.Data.Config
{
    public class SecretConfiguration : IEntityTypeConfiguration<Secret>
    {
        public void Configure(EntityTypeBuilder<Secret> builder)
        {
            builder.ToTable("Secret");

            builder.HasKey(m => m.Id).IsClustered(true);

            builder.HasMany(m => m.AIModels)
                .WithOne(m => m.Secret)
                .HasForeignKey(m => m.SecretId);
        }
    }
}
