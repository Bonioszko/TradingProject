using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PaperTrade.Trading.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(message => message.MessageId);
        builder.Property(message => message.ContractType).HasMaxLength(200);
        builder.Property(message => message.Exchange).HasMaxLength(200);
        builder.Property(message => message.RoutingKey).HasMaxLength(200);
        builder.Property(message => message.Content).HasColumnType("jsonb");
        builder.Property(message => message.TraceParent).HasMaxLength(256);
        builder.Property(message => message.LeaseId).IsConcurrencyToken();
        builder.Property(message => message.LastErrorCode).HasMaxLength(64);
        builder.Property(message => message.LastError).HasMaxLength(2_000);
        builder.HasIndex(message => message.AggregateId);
        builder.HasIndex(message => new
        {
            message.PublishedAt,
            message.SuspendedAt,
            message.NextAttemptAt,
            message.LeaseExpiresAt,
            message.OccurredAt
        });
    }
}
