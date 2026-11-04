using TicketDesk.Api.Dtos.Tiers;

namespace TicketDesk.Api.Dtos.Events;

public class EventResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Venue { get; set; } = string.Empty;
    public DateTime StartsAtUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<TierResponse> Tiers { get; set; } = [];
}
