using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMAIS.WKFLO.Common.MD.Domain.Data.Config
{
    public class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
    {
        public void Configure(EntityTypeBuilder<Conversation> builder)
        {
            builder.ToTable("Conversation");
            builder.HasKey(m => m.Id).IsClustered(true);

            builder.Property(e => e.SessionId).HasMaxLength(500).IsRequired();
            builder.Property(e => e.Name).HasMaxLength(300);
            builder.Property(e => e.PreviewLine).HasMaxLength(300);
            builder.Property(e => e.ConversationDate);
            builder.Property(e => e.UserId).HasMaxLength(500);
            builder.Property(e => e.SubjectId).HasMaxLength(500);
            builder.Property(e => e.TotalThumbsUp).HasDefaultValue(0).IsRequired();
            builder.Property(e => e.TotalThumbsDown).HasDefaultValue(0).IsRequired();
            builder.Property(e => e.LastActivityAt);
            builder.Property(e => e.IsResolved).HasDefaultValue(true);
            builder.Property(e => e.AnalysisStatus).HasConversion<int>().HasDefaultValue(ConversationAnalysisStatus.Pending);
            builder.Property(e => e.TitleStatus).HasConversion<int>().HasDefaultValue(ConversationTitleStatus.Pending);

            builder.HasOne(m => m.Agent).WithMany().HasForeignKey(m => m.AgentId);
            builder.HasOne(m => m.Tenant).WithMany().HasForeignKey(m => m.TenantId);
            builder.HasMany(m => m.ConversationAnalyses).WithOne(m => m.Conversation).HasForeignKey(m => m.ConversationId);
        }
    }
}
