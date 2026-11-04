namespace TicketDesk.Api.Services;

public class ReservationOptions
{
    public const string SectionName = "Reservations";

    public int HoldDurationMinutes { get; set; } = 10;
    public int MaxQuantityPerHold { get; set; } = 6;
    public int MaxActiveTicketsPerUserPerTier { get; set; } = 6;
    public int MaxConcurrencyRetries { get; set; } = 3;
}
