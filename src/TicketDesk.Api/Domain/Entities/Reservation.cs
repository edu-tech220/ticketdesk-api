using TicketDesk.Api.Domain.Enums;

namespace TicketDesk.Api.Domain.Entities;

public class Reservation
{
    public Guid Id { get; set; }
    public Guid TicketTierId { get; set; }
    public Guid UserId { get; set; }
    public int Quantity { get; set; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Held;
    public DateTime HeldAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public int Version { get; set; }

    public TicketTier? TicketTier { get; set; }
}
