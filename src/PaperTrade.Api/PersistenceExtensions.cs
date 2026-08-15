using Microsoft.EntityFrameworkCore;
using PaperTrade.Trading.Persistence;

namespace PaperTrade.Api;

internal static class PersistenceExtensions
{
    public static async Task ApplyTradingMigrationsAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TradingDbContext>();
        await context.Database.MigrateAsync();
    }
}
