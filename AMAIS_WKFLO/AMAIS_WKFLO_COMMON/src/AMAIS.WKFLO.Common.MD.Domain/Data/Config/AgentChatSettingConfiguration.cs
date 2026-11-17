using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMAIS.WKFLO.Common.MD.Domain.Data.Config
{
    public class AgentChatSettingConfiguration : IEntityTypeConfiguration<AgentChatSetting>
    {
        public void Configure(EntityTypeBuilder<AgentChatSetting> builder)
        {
            builder.ToTable("AgentChatSetting");

            builder.HasKey(m => m.Id).IsClustered(true);

            builder.HasOne(m => m.Agent);

            builder.HasMany(m => m.PredefinedPrompts)
                .WithOne(m => m.AgentChatSetting)
                .HasForeignKey(m => m.AgentChatSettingId);
        }
    }
}
