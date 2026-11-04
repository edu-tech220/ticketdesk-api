using TicketDesk.Api.Data;
using TicketDesk.Api.Domain.Entities;
using TicketDesk.Api.Domain.Enums;

namespace TicketDesk.Tests.TestUtils;

public static class TestDataBuilder
{
    public static Event CreatePublishedEvent(AppDbContext db, DateTime? startsAtUtc = null)
    {
        var ev = new Event
        {
            Id = Guid.NewGuid(),
            Name = "Concierto de prueba",
            Venue = "Foro de prueba",
            StartsAtUtc = startsAtUtc ?? DateTime.UtcNow.AddDays(30),
            Status = EventStatus.Published,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow
        };

        db.Events.Add(ev);
        db.SaveChanges();
        return ev;
    }

    public static TicketTier CreateTier(AppDbContext db, Guid eventId, int quota, decimal price = 500m)
    {
        var tier = new TicketTier
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Name = "General",
            Price = price,
            Quota = quota,
            ReservedCount = 0,
            Version = 0
        };

        db.TicketTiers.Add(tier);
        db.SaveChanges();
        return tier;
    }
}
