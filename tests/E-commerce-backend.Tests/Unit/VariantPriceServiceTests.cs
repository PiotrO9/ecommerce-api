using E_commerce_backend.Data;
using E_commerce_backend.Services.Pricing;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Tests.Unit;

public sealed class VariantPriceServiceTests
{
    [Fact]
    public async Task InvalidCommandsAreRejectedBeforeOpeningDatabase()
    {
        await using var database = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused").Options);
        var service = new VariantPriceService(database, TimeProvider.System);
        var valid = new ChangeVariantPrice(Guid.NewGuid(), 10m, "PLN", "Zmiana", null,
            Guid.NewGuid(), Guid.NewGuid());
        ChangeVariantPrice[] invalid =
        [
            valid with { VariantId = Guid.Empty }, valid with { ActorUserId = Guid.Empty },
            valid with { OperationKey = Guid.Empty }, valid with { ExpectedPriceChangeId = Guid.Empty },
            valid with { BaseAmount = -1 }, valid with { BaseAmount = 1.001m },
            valid with { BaseAmount = 10000000000000000m }, valid with { Currency = "EUR" },
            valid with { Currency = " " }, valid with { Reason = null },
            valid with { Reason = "  " }, valid with { Reason = new string('x', 121) }
        ];
        foreach (var command in invalid)
        {
            var result = await service.ChangeAsync(command);
            Assert.Equal(PriceChangeStatus.InvalidRequest, result.Status);
            Assert.Null(result.Price);
        }
    }
}
