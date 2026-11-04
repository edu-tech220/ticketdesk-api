using TicketDesk.Api.Domain.Enums;

namespace TicketDesk.Api.Domain.Entities;

public class Event
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Venue { get; set; } = string.Empty;
    public DateTime StartsAtUtc { get; set; }
    public EventStatus Status { get; set; } = EventStatus.Draft;
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public List<TicketTier> Tiers { get; set; } = [];
}
