namespace TicketDesk.Api.Domain.Entities;

public class TicketTier
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quota { get; set; }
    public int ReservedCount { get; set; }

    /// <summary>
    /// Token de concurrencia optimista administrado por la aplicacion.
    /// Se incrementa en cada actualizacion de ReservedCount para detectar
    /// pujas/reservas concurrentes sobre la misma fila (portatil entre SQL Server y SQLite).
    /// </summary>
    public int Version { get; set; }

    public Event? Event { get; set; }

    public int Available => Quota - ReservedCount;
}
