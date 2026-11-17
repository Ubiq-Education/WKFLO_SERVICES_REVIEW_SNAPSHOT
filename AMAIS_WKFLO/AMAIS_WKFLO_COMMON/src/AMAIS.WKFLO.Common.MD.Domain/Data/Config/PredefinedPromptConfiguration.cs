using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMAIS.WKFLO.Common.MD.Domain.Data.Config
{
    public class PredefinedPromptConfiguration : IEntityTypeConfiguration<PredefinedPrompt>
    {
        public void Configure(EntityTypeBuilder<PredefinedPrompt> builder)
        {
            builder.ToTable("PredefinedPrompt");

            builder.HasKey(m => m.Id).IsClustered(true);

            builder.Property(m => m.Title)
                .HasMaxLength(200);

            builder.HasOne(m => m.AgentChatSetting)
                .WithMany(m => m.PredefinedPrompts)
                .HasForeignKey(m => m.AgentChatSettingId);
        }
    }
}
