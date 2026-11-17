using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMAIS.WKFLO.Common.MD.Domain.Data.Config
{
    public class WebOriginConfiguration : IEntityTypeConfiguration<WebOrigin>
    {
        public void Configure(EntityTypeBuilder<WebOrigin> builder)
        {
            builder.ToTable("WebOrigin");

            builder.HasKey(m => m.Id).IsClustered(true);

            builder.HasOne(m => m.Tenant);
        }
    }
}
