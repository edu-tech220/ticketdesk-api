using TicketDesk.Api.Dtos.Common;
using TicketDesk.Api.Dtos.Events;
using TicketDesk.Api.Dtos.Tiers;

namespace TicketDesk.Api.Services;

public interface IEventService
{
    Task<PagedResult<EventResponse>> GetPagedAsync(EventQueryParameters query, CancellationToken cancellationToken);
    Task<EventResponse> GetByIdAsync(Guid eventId, CancellationToken cancellationToken);
    Task<EventResponse> CreateAsync(CreateEventRequest request, Guid createdByUserId, CancellationToken cancellationToken);
    Task<EventResponse> AddTierAsync(Guid eventId, CreateTierRequest request, CancellationToken cancellationToken);
    Task<EventResponse> PublishAsync(Guid eventId, CancellationToken cancellationToken);
    Task<EventResponse> CancelAsync(Guid eventId, CancellationToken cancellationToken);
}
