using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMAIS.WKFLO.Common.MD.Domain.Data.Config
{
    public class ConversationAnalysisConfiguration : IEntityTypeConfiguration<ConversationAnalysis>
    {
        public void Configure(EntityTypeBuilder<ConversationAnalysis> builder)
        {
            builder.ToTable("ConversationAnalysis");
            builder.HasKey(m => m.Id).IsClustered(true);

            builder.HasDiscriminator<string>("AnalysisType")
                .HasValue<ResolvedConversationAnalysisResult>("Resolved");

            builder.Property("AnalysisType").HasMaxLength(100).IsRequired();
            builder.Property(e => e.AnalyzedAt).IsRequired();
        }
    }
}
