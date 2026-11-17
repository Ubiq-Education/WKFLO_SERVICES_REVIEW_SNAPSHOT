using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMAIS.WKFLO.Common.MD.Domain.Data.Config
{
    public class AIModelConfiguration : IEntityTypeConfiguration<AIModel>
    {
        public void Configure(EntityTypeBuilder<AIModel> builder)
        {
            builder.ToTable("AIModel");

            builder.HasKey(m => m.Id).IsClustered(true);

            builder.HasOne(m => m.Secret);

            builder.HasMany(m => m.Agents)
                .WithOne(m => m.AIModel)
                .HasForeignKey(m => m.AIModelId);
        }
    }
}
