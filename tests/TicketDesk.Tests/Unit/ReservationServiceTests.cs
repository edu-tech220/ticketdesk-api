using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TicketDesk.Api.Common;
using TicketDesk.Api.Domain.Entities;
using TicketDesk.Api.Domain.Enums;
using TicketDesk.Api.Dtos.Reservations;
using TicketDesk.Api.Services;
using TicketDesk.Tests.TestUtils;
using Xunit;

namespace TicketDesk.Tests.Unit;

public class ReservationServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    private static ReservationOptions DefaultOptions() => new()
    {
        HoldDurationMinutes = 10,
        MaxQuantityPerHold = 6,
        MaxActiveTicketsPerUserPerTier = 6,
        MaxConcurrencyRetries = 5
    };

    private ReservationService CreateService(ReservationOptions? options = null) =>
        new(_database.CreateContext(), Options.Create(options ?? DefaultOptions()));

    [Fact]
    public async Task CreateHoldAsync_Succeeds_WhenCapacityAvailable()
    {
        using var seedDb = _database.CreateContext();
        var ev = TestDataBuilder.CreatePublishedEvent(seedDb);
        var tier = TestDataBuilder.CreateTier(seedDb, ev.Id, quota: 10);

        var service = CreateService();
        var userId = Guid.NewGuid();

        var result = await service.CreateHoldAsync(userId, new CreateHoldRequest { TicketTierId = tier.Id, Quantity = 2 }, CancellationToken.None);

        Assert.Equal(ReservationStatus.Held.ToString(), result.Status);

        using var verifyDb = _database.CreateContext();
        var reloadedTier = await verifyDb.TicketTiers.SingleAsync(t => t.Id == tier.Id);
        Assert.Equal(8, reloadedTier.Available);
    }

    [Fact]
    public async Task CreateHoldAsync_Throws_WhenNotEnoughAvailability()
    {
        using var seedDb = _database.CreateContext();
        var ev = TestDataBuilder.CreatePublishedEvent(seedDb);
        var tier = TestDataBuilder.CreateTier(seedDb, ev.Id, quota: 1);

        var service = CreateService();
        await service.CreateHoldAsync(Guid.NewGuid(), new CreateHoldRequest { TicketTierId = tier.Id, Quantity = 1 }, CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateHoldAsync(Guid.NewGuid(), new CreateHoldRequest { TicketTierId = tier.Id, Quantity = 1 }, CancellationToken.None));
    }

    [Fact]
    public async Task CreateHoldAsync_Throws_WhenEventNotPublished()
    {
        using var seedDb = _database.CreateContext();
        var ev = TestDataBuilder.CreatePublishedEvent(seedDb);
        ev.Status = EventStatus.Draft;
        seedDb.SaveChanges();
        var tier = TestDataBuilder.CreateTier(seedDb, ev.Id, quota: 5);

        var service = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateHoldAsync(Guid.NewGuid(), new CreateHoldRequest { TicketTierId = tier.Id, Quantity = 1 }, CancellationToken.None));
    }

    [Fact]
    public async Task CreateHoldAsync_Throws_WhenQuantityExceedsMaxPerHold()
    {
        using var seedDb = _database.CreateContext();
        var ev = TestDataBuilder.CreatePublishedEvent(seedDb);
        var tier = TestDataBuilder.CreateTier(seedDb, ev.Id, quota: 100);

        var service = CreateService(new ReservationOptions { MaxQuantityPerHold = 6, MaxActiveTicketsPerUserPerTier = 50, MaxConcurrencyRetries = 3 });

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateHoldAsync(Guid.NewGuid(), new CreateHoldRequest { TicketTierId = tier.Id, Quantity = 7 }, CancellationToken.None));
    }

    [Fact]
    public async Task CreateHoldAsync_Throws_WhenUserExceedsPerTierCap()
    {
        using var seedDb = _database.CreateContext();
        var ev = TestDataBuilder.CreatePublishedEvent(seedDb);
        var tier = TestDataBuilder.CreateTier(seedDb, ev.Id, quota: 100);

        var options = new ReservationOptions { MaxQuantityPerHold = 10, MaxActiveTicketsPerUserPerTier = 6, MaxConcurrencyRetries = 3 };
        var service = CreateService(options);
        var userId = Guid.NewGuid();

        await service.CreateHoldAsync(userId, new CreateHoldRequest { TicketTierId = tier.Id, Quantity = 6 }, CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateHoldAsync(userId, new CreateHoldRequest { TicketTierId = tier.Id, Quantity = 1 }, CancellationToken.None));
    }

    [Fact]
    public async Task ConfirmAsync_Succeeds_ForOwnerWithActiveHold()
    {
        using var seedDb = _database.CreateContext();
        var ev = TestDataBuilder.CreatePublishedEvent(seedDb);
        var tier = TestDataBuilder.CreateTier(seedDb, ev.Id, quota: 5);

        var service = CreateService();
        var userId = Guid.NewGuid();
        var hold = await service.CreateHoldAsync(userId, new CreateHoldRequest { TicketTierId = tier.Id, Quantity = 2 }, CancellationToken.None);

        var confirmed = await service.ConfirmAsync(userId, hold.Id, CancellationToken.None);

        Assert.Equal(ReservationStatus.Confirmed.ToString(), confirmed.Status);
        Assert.NotNull(confirmed.ConfirmedAtUtc);
    }

    [Fact]
    public async Task ConfirmAsync_Throws_ForDifferentUser()
    {
        using var seedDb = _database.CreateContext();
        var ev = TestDataBuilder.CreatePublishedEvent(seedDb);
        var tier = TestDataBuilder.CreateTier(seedDb, ev.Id, quota: 5);

        var service = CreateService();
        var owner = Guid.NewGuid();
        var hold = await service.CreateHoldAsync(owner, new CreateHoldRequest { TicketTierId = tier.Id, Quantity = 1 }, CancellationToken.None);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.ConfirmAsync(Guid.NewGuid(), hold.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ConfirmAsync_ReleasesCapacityAndThrows_WhenHoldAlreadyExpired()
    {
        using var seedDb = _database.CreateContext();
        var ev = TestDataBuilder.CreatePublishedEvent(seedDb);
        var tier = TestDataBuilder.CreateTier(seedDb, ev.Id, quota: 5);
        var userId = Guid.NewGuid();

        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            TicketTierId = tier.Id,
            UserId = userId,
            Quantity = 2,
            Status = ReservationStatus.Held,
            HeldAtUtc = DateTime.UtcNow.AddMinutes(-20),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-10),
            Version = 0
        };
        tier.ReservedCount = 2;
        seedDb.Reservations.Add(reservation);
        seedDb.SaveChanges();

        var service = CreateService();

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.ConfirmAsync(userId, reservation.Id, CancellationToken.None));

        using var verifyDb = _database.CreateContext();
        var reloadedReservation = await verifyDb.Reservations.SingleAsync(r => r.Id == reservation.Id);
        var reloadedTier = await verifyDb.TicketTiers.SingleAsync(t => t.Id == tier.Id);
        Assert.Equal(ReservationStatus.Expired, reloadedReservation.Status);
        Assert.Equal(5, reloadedTier.Available);
    }

    [Fact]
    public async Task CancelAsync_ReleasesCapacity_WhenOwnerCancelsHeldReservation()
    {
        using var seedDb = _database.CreateContext();
        var ev = TestDataBuilder.CreatePublishedEvent(seedDb);
        var tier = TestDataBuilder.CreateTier(seedDb, ev.Id, quota: 5);

        var service = CreateService();
        var userId = Guid.NewGuid();
        var hold = await service.CreateHoldAsync(userId, new CreateHoldRequest { TicketTierId = tier.Id, Quantity = 3 }, CancellationToken.None);

        var cancelled = await service.CancelAsync(userId, isAdmin: false, hold.Id, CancellationToken.None);

        Assert.Equal(ReservationStatus.Cancelled.ToString(), cancelled.Status);

        using var verifyDb = _database.CreateContext();
        var reloadedTier = await verifyDb.TicketTiers.SingleAsync(t => t.Id == tier.Id);
        Assert.Equal(5, reloadedTier.Available);
    }

    [Fact]
    public async Task CancelAsync_Throws_WhenCustomerTriesToCancelConfirmedReservation()
    {
        using var seedDb = _database.CreateContext();
        var ev = TestDataBuilder.CreatePublishedEvent(seedDb);
        var tier = TestDataBuilder.CreateTier(seedDb, ev.Id, quota: 5);

        var service = CreateService();
        var userId = Guid.NewGuid();
        var hold = await service.CreateHoldAsync(userId, new CreateHoldRequest { TicketTierId = tier.Id, Quantity = 1 }, CancellationToken.None);
        await service.ConfirmAsync(userId, hold.Id, CancellationToken.None);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.CancelAsync(userId, isAdmin: false, hold.Id, CancellationToken.None));
    }

    [Fact]
    public async Task CancelAsync_Succeeds_WhenAdminCancelsConfirmedReservation()
    {
        using var seedDb = _database.CreateContext();
        var ev = TestDataBuilder.CreatePublishedEvent(seedDb);
        var tier = TestDataBuilder.CreateTier(seedDb, ev.Id, quota: 5);

        var service = CreateService();
        var userId = Guid.NewGuid();
        var hold = await service.CreateHoldAsync(userId, new CreateHoldRequest { TicketTierId = tier.Id, Quantity = 2 }, CancellationToken.None);
        await service.ConfirmAsync(userId, hold.Id, CancellationToken.None);

        var cancelled = await service.CancelAsync(Guid.NewGuid(), isAdmin: true, hold.Id, CancellationToken.None);

        Assert.Equal(ReservationStatus.Cancelled.ToString(), cancelled.Status);

        using var verifyDb = _database.CreateContext();
        var reloadedTier = await verifyDb.TicketTiers.SingleAsync(t => t.Id == tier.Id);
        Assert.Equal(5, reloadedTier.Available);
    }

    [Fact]
    public async Task ExpireDueHoldsAsync_ReleasesOnlyExpiredHolds()
    {
        using var seedDb = _database.CreateContext();
        var ev = TestDataBuilder.CreatePublishedEvent(seedDb);
        var tier = TestDataBuilder.CreateTier(seedDb, ev.Id, quota: 10);

        var expired = new Reservation
        {
            Id = Guid.NewGuid(),
            TicketTierId = tier.Id,
            UserId = Guid.NewGuid(),
            Quantity = 3,
            Status = ReservationStatus.Held,
            HeldAtUtc = DateTime.UtcNow.AddMinutes(-20),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5)
        };
        var active = new Reservation
        {
            Id = Guid.NewGuid(),
            TicketTierId = tier.Id,
            UserId = Guid.NewGuid(),
            Quantity = 2,
            Status = ReservationStatus.Held,
            HeldAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10)
        };
        tier.ReservedCount = 5;
        seedDb.Reservations.AddRange(expired, active);
        seedDb.SaveChanges();

        var service = CreateService();
        var released = await service.ExpireDueHoldsAsync(CancellationToken.None);

        Assert.Equal(1, released);

        using var verifyDb = _database.CreateContext();
        var reloadedTier = await verifyDb.TicketTiers.SingleAsync(t => t.Id == tier.Id);
        var reloadedExpired = await verifyDb.Reservations.SingleAsync(r => r.Id == expired.Id);
        var reloadedActive = await verifyDb.Reservations.SingleAsync(r => r.Id == active.Id);

        Assert.Equal(ReservationStatus.Expired, reloadedExpired.Status);
        Assert.Equal(ReservationStatus.Held, reloadedActive.Status);
        Assert.Equal(8, reloadedTier.Available); // quota 10 - los 2 de la reserva activa (el expirado ya libero su cupo)
    }

    [Fact]
    public async Task CreateHoldAsync_NeverOversells_UnderConcurrentRequests()
    {
        using var seedDb = _database.CreateContext();
        var ev = TestDataBuilder.CreatePublishedEvent(seedDb);
        const int quota = 5;
        var tier = TestDataBuilder.CreateTier(seedDb, ev.Id, quota: quota);

        const int concurrentBuyers = 20;
        var tasks = Enumerable.Range(0, concurrentBuyers).Select(async _ =>
        {
            var service = CreateService();
            try
            {
                await service.CreateHoldAsync(Guid.NewGuid(), new CreateHoldRequest { TicketTierId = tier.Id, Quantity = 1 }, CancellationToken.None);
                return true;
            }
            catch (ConflictException)
            {
                return false;
            }
        });

        var results = await Task.WhenAll(tasks);

        Assert.Equal(quota, results.Count(success => success));

        using var verifyDb = _database.CreateContext();
        var reloadedTier = await verifyDb.TicketTiers.SingleAsync(t => t.Id == tier.Id);
        var heldCount = await verifyDb.Reservations
            .Where(r => r.TicketTierId == tier.Id && r.Status == ReservationStatus.Held)
            .SumAsync(r => r.Quantity);

        Assert.Equal(0, reloadedTier.Available);
        Assert.Equal(quota, heldCount);
    }

    public void Dispose() => _database.Dispose();
}
