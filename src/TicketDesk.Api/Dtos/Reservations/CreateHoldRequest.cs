using System.ComponentModel.DataAnnotations;

namespace TicketDesk.Api.Dtos.Reservations;

public class CreateHoldRequest
{
    [Required]
    public Guid TicketTierId { get; set; }

    [Range(1, 100)]
    public int Quantity { get; set; } = 1;
}
