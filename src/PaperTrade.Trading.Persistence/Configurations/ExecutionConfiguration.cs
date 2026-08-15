using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaperTrade.Domain.Executions;
using PaperTrade.Domain.Orders;

namespace PaperTrade.Trading.Persistence.Configurations;

internal sealed class ExecutionConfiguration : IEntityTypeConfiguration<Execution>
{
    public void Configure(EntityTypeBuilder<Execution> builder)
    {
        builder.ToTable("executions");
        builder.HasKey(execution => execution.Id);
        builder.HasIndex(execution => execution.OrderId).IsUnique();
        builder.Property(execution => execution.Quantity).HasPrecision(28, 8);
        builder.Property(execution => execution.Price).HasPrecision(19, 4);
        builder.Property(execution => execution.Commission).HasPrecision(19, 4);

        builder.HasOne<Order>()
            .WithOne()
            .HasForeignKey<Execution>(execution => execution.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
