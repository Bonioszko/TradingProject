using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaperTrade.Domain.Instruments;

namespace PaperTrade.Trading.Persistence.Configurations;

internal sealed class InstrumentConfiguration : IEntityTypeConfiguration<Instrument>
{
    public void Configure(EntityTypeBuilder<Instrument> builder)
    {
        builder.ToTable("instruments");
        builder.HasKey(instrument => instrument.Id);
        builder.HasIndex(instrument => instrument.Symbol).IsUnique();
        builder.Property(instrument => instrument.Symbol).HasMaxLength(12);
        builder.Property(instrument => instrument.Name).HasMaxLength(100);
        builder.Property(instrument => instrument.MarketPrice).HasPrecision(19, 4);
        builder.Property(instrument => instrument.Currency).HasMaxLength(3).IsFixedLength();

        builder.HasData(
            new
            {
                Id = SeedData.MicrosoftInstrumentId,
                Symbol = "MSFT",
                Name = "Microsoft Corporation",
                MarketPrice = 400m,
                Currency = "USD"
            },
            new
            {
                Id = SeedData.AppleInstrumentId,
                Symbol = "AAPL",
                Name = "Apple Inc.",
                MarketPrice = 200m,
                Currency = "USD"
            },
            new
            {
                Id = SeedData.NvidiaInstrumentId,
                Symbol = "NVDA",
                Name = "NVIDIA Corporation",
                MarketPrice = 150m,
                Currency = "USD"
            });
    }
}
