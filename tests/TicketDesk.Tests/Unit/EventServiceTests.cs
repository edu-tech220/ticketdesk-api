using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TicketDesk.Api.Common;
using TicketDesk.Api.Domain.Enums;
using TicketDesk.Api.Dtos.Events;
using TicketDesk.Api.Dtos.Reservations;
using TicketDesk.Api.Dtos.Tiers;
using TicketDesk.Api.Services;
using TicketDesk.Tests.TestUtils;
using Xunit;

namespace TicketDesk.Tests.Unit;

public class EventServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    private EventService CreateService() => new(_database.CreateContext());

    [Fact]
    public async Task CreateAsync_Throws_WhenStartsInThePast()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(new CreateEventRequest
            {
                Name = "Evento",
                Venue = "Lugar",
                StartsAtUtc = DateTime.UtcNow.AddDays(-1)
            }, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task PublishAsync_Throws_WhenEventHasNoTiers()
    {
        var service = CreateService();
        var created = await service.CreateAsync(new CreateEventRequest
        {
            Name = "Evento",
            Venue = "Lugar",
            StartsAtUtc = DateTime.UtcNow.AddDays(10)
        }, Guid.NewGuid(), CancellationToken.None);

        await Assert.ThrowsAsync<ValidationException>(() => service.PublishAsync(created.Id, CancellationToken.None));
    }

    [Fact]
    public async Task PublishAsync_Succeeds_WhenEventHasAtLeastOneTier()
    {
        var service = CreateService();
        var created = await service.CreateAsync(new CreateEventRequest
        {
            Name = "Evento",
            Venue = "Lugar",
            StartsAtUtc = DateTime.UtcNow.AddDays(10)
        }, Guid.NewGuid(), CancellationToken.None);

        await service.AddTierAsync(created.Id, new CreateTierRequest { Name = "General", Price = 100, Quota = 50 }, CancellationToken.None);
        var published = await service.PublishAsync(created.Id, CancellationToken.None);

        Assert.Equal(EventStatus.Published.ToString(), published.Status);
    }

    [Fact]
    public async Task AddTierAsync_Throws_WhenEventIsNotDraft()
    {
        var service = CreateService();
        var created = await service.CreateAsync(new CreateEventRequest
        {
            Name = "Evento",
            Venue = "Lugar",
            StartsAtUtc = DateTime.UtcNow.AddDays(10)
        }, Guid.NewGuid(), CancellationToken.None);

        await service.AddTierAsync(created.Id, new CreateTierRequest { Name = "General", Price = 100, Quota = 50 }, CancellationToken.None);
        await service.PublishAsync(created.Id, CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.AddTierAsync(created.Id, new CreateTierRequest { Name = "VIP", Price = 200, Quota = 10 }, CancellationToken.None));
    }

    [Fact]
    public async Task CancelAsync_CancelsActiveReservations_AndResetsAvailability()
    {
        var service = CreateService();
        var created = await service.CreateAsync(new CreateEventRequest
        {
            Name = "Evento",
            Venue = "Lugar",
            StartsAtUtc = DateTime.UtcNow.AddDays(10)
        }, Guid.NewGuid(), CancellationToken.None);
        var withTier = await service.AddTierAsync(created.Id, new CreateTierRequest { Name = "General", Price = 100, Quota = 50 }, CancellationToken.None);
        await service.PublishAsync(created.Id, CancellationToken.None);

        var tierId = withTier.Tiers.Single().Id;
        var reservationOptions = Options.Create(new ReservationOptions());
        var reservationService = new ReservationService(_database.CreateContext(), reservationOptions);
        await reservationService.CreateHoldAsync(Guid.NewGuid(), new CreateHoldRequest
        {
            TicketTierId = tierId,
            Quantity = 5
        }, CancellationToken.None);

        var cancelled = await service.CancelAsync(created.Id, CancellationToken.None);

        Assert.Equal(EventStatus.Cancelled.ToString(), cancelled.Status);
        Assert.Equal(50, cancelled.Tiers.Single().Available);

        using var verifyDb = _database.CreateContext();
        var anyActiveReservation = await verifyDb.Reservations
            .AnyAsync(r => r.TicketTierId == tierId && r.Status == ReservationStatus.Held);
        Assert.False(anyActiveReservation);
    }

    public void Dispose() => _database.Dispose();
}
