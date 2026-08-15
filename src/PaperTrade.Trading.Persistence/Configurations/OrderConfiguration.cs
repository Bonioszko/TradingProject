using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaperTrade.Domain.Accounts;
using PaperTrade.Domain.Instruments;
using PaperTrade.Domain.Orders;

namespace PaperTrade.Trading.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(order => order.Id);
        builder.Property(order => order.Side).HasConversion<string>().HasMaxLength(8);
        builder.Property(order => order.Type).HasConversion<string>().HasMaxLength(8);
        builder.Property(order => order.Quantity).HasPrecision(28, 8);
        builder.Property(order => order.LimitPrice).HasPrecision(19, 4);
        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(12);
        builder.Property(order => order.RejectionReason).HasMaxLength(500);
        builder.Property(order => order.Version).IsConcurrencyToken();
        builder.HasIndex(order => order.AccountId);
        builder.HasIndex(order => order.InstrumentId);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(order => order.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(order => order.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
