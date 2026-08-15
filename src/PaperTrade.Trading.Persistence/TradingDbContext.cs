using Microsoft.EntityFrameworkCore;
using PaperTrade.Domain.Accounts;
using PaperTrade.Domain.Executions;
using PaperTrade.Domain.Instruments;
using PaperTrade.Domain.Orders;

namespace PaperTrade.Trading.Persistence;

public sealed class TradingDbContext(DbContextOptions<TradingDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Execution> Executions => Set<Execution>();

    public DbSet<Instrument> Instruments => Set<Instrument>();

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("trading");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TradingDbContext).Assembly);
    }
}
