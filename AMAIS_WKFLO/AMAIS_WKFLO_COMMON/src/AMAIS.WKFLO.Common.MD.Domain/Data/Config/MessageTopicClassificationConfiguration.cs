using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMAIS.WKFLO.Common.MD.Domain.Data.Config
{
    public class MessageTopicClassificationConfiguration : IEntityTypeConfiguration<MessageTopicClassification>
    {
        public void Configure(EntityTypeBuilder<MessageTopicClassification> builder)
        {
            builder.ToTable("MessageTopicClassification");

            builder.HasKey(m => m.Id).IsClustered(true);

            builder.Property(m => m.TopicId);
            builder.Property(m => m.TopicName).HasMaxLength(300);
            builder.Property(m => m.SessionId).HasMaxLength(500).IsRequired();
            builder.Property(m => m.MessageId).IsRequired();

            builder.Property(m => m.MessageDate).IsRequired();
            builder.Property(m => m.IsResolved);
            builder.Property(m => m.TopicVersion).HasMaxLength(50);
            builder.Property(m => m.AnswerMessageId);

            builder.HasOne(m => m.Agent).WithMany().HasForeignKey(m => m.AgentId);

            builder.HasIndex(m => m.MessageId).IsUnique();
        }
    }
}
