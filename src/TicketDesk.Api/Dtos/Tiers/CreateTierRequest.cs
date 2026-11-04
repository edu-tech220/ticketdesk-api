using System.ComponentModel.DataAnnotations;

namespace TicketDesk.Api.Dtos.Tiers;

public class CreateTierRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Range(0, 1_000_000)]
    public decimal Price { get; set; }

    [Range(1, 100_000)]
    public int Quota { get; set; }
}
