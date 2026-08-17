using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PaperTrade.Trading.Persistence.Configurations;

internal sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("inbox_messages");
        builder.HasKey(message => message.MessageId);
        builder.Property(message => message.Consumer).HasMaxLength(128);
        builder.HasIndex(message => new { message.Consumer, message.ProcessedAt });
    }
}
