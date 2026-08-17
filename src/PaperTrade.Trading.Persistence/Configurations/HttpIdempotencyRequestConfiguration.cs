using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaperTrade.Domain.Orders;

namespace PaperTrade.Trading.Persistence.Configurations;

internal sealed class HttpIdempotencyRequestConfiguration : IEntityTypeConfiguration<HttpIdempotencyRequest>
{
    public void Configure(EntityTypeBuilder<HttpIdempotencyRequest> builder)
    {
        builder.ToTable("http_idempotency_requests");
        builder.HasKey(request => request.Key);
        builder.Property(request => request.Key).HasMaxLength(128);
        builder.Property(request => request.RequestHash).HasMaxLength(64).IsFixedLength();
        builder.HasIndex(request => request.OrderId).IsUnique();
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(request => request.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
