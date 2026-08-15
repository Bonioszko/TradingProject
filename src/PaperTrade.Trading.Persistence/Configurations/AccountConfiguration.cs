using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaperTrade.Domain.Accounts;

namespace PaperTrade.Trading.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts");
        builder.HasKey(account => account.Id);
        builder.Property(account => account.AvailableCash).HasPrecision(19, 4);
        builder.Property(account => account.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(account => account.Version).IsConcurrencyToken();

        builder.HasData(new
        {
            Id = SeedData.AccountId,
            AvailableCash = 100_000m,
            Currency = "USD",
            Version = 0L
        });
    }
}
