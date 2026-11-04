using TicketDesk.Api.Dtos.Common;
using TicketDesk.Api.Dtos.Reservations;

namespace TicketDesk.Api.Services;

public interface IReservationService
{
    Task<ReservationResponse> CreateHoldAsync(Guid userId, CreateHoldRequest request, CancellationToken cancellationToken);
    Task<ReservationResponse> ConfirmAsync(Guid userId, Guid reservationId, CancellationToken cancellationToken);
    Task<ReservationResponse> CancelAsync(Guid userId, bool isAdmin, Guid reservationId, CancellationToken cancellationToken);
    Task<PagedResult<ReservationResponse>> GetMineAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// Libera los holds vencidos (ExpiresAtUtc en el pasado) devolviendo su cupo al nivel de boletos.
    /// Devuelve la cantidad de reservas liberadas. Pensado para ser invocado periodicamente por un BackgroundService.
    /// </summary>
    Task<int> ExpireDueHoldsAsync(CancellationToken cancellationToken);
}
