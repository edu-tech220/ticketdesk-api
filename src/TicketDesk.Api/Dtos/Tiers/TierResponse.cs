namespace TicketDesk.Api.Dtos.Tiers;

public class TierResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quota { get; set; }
    public int Available { get; set; }
}
