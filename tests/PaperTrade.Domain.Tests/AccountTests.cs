using PaperTrade.Domain.Accounts;

namespace PaperTrade.Domain.Tests;

public sealed class AccountTests
{
    [Fact]
    public void DebitReducesAvailableCashAndAdvancesVersion()
    {
        var account = Account.Open(Guid.NewGuid(), 1_000m, "usd");

        account.Debit(250m);

        Assert.Equal(750m, account.AvailableCash);
        Assert.Equal("USD", account.Currency);
        Assert.Equal(1, account.Version);
    }

    [Fact]
    public void DebitWithInsufficientCashDoesNotChangeAccount()
    {
        var account = Account.Open(Guid.NewGuid(), 100m, "USD");

        Assert.Throws<InvalidOperationException>(() => account.Debit(101m));
        Assert.Equal(100m, account.AvailableCash);
        Assert.Equal(0, account.Version);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DebitWithNonPositiveAmountThrows(decimal amount)
    {
        var account = Account.Open(Guid.NewGuid(), 100m, "USD");

        Assert.Throws<ArgumentOutOfRangeException>(() => account.Debit(amount));
    }
}
