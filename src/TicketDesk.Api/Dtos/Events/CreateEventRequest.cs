using System.ComponentModel.DataAnnotations;

namespace TicketDesk.Api.Dtos.Events;

public class CreateEventRequest
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Venue { get; set; } = string.Empty;

    [Required]
    public DateTime StartsAtUtc { get; set; }
}
