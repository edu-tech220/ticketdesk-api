using Microsoft.EntityFrameworkCore;
using TicketDesk.Api.Common;
using TicketDesk.Api.Data;
using TicketDesk.Api.Domain.Entities;
using TicketDesk.Api.Domain.Enums;
using TicketDesk.Api.Dtos.Common;
using TicketDesk.Api.Dtos.Events;
using TicketDesk.Api.Dtos.Tiers;

namespace TicketDesk.Api.Services;

public class EventService(AppDbContext db) : IEventService
{
    public async Task<PagedResult<EventResponse>> GetPagedAsync(EventQueryParameters query, CancellationToken cancellationToken)
    {
        var baseQuery = db.Events.AsNoTracking().Include(e => e.Tiers).AsQueryable();

        if (query.Status.HasValue)
        {
            baseQuery = baseQuery.Where(e => e.Status == query.Status.Value);
        }

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var events = await baseQuery
            .OrderBy(e => e.StartsAtUtc)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<EventResponse>
        {
            Items = events.Select(ToResponse).ToList(),
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = totalCount
        };
    }

    public async Task<EventResponse> GetByIdAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var ev = await db.Events.AsNoTracking().Include(e => e.Tiers)
            .FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken)
            ?? throw new NotFoundException("El evento no existe.");

        return ToResponse(ev);
    }

    public async Task<EventResponse> CreateAsync(CreateEventRequest request, Guid createdByUserId, CancellationToken cancellationToken)
    {
        if (request.StartsAtUtc <= DateTime.UtcNow)
        {
            throw new ValidationException("La fecha del evento debe ser futura.");
        }

        var ev = new Event
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Venue = request.Venue.Trim(),
            StartsAtUtc = request.StartsAtUtc,
            Status = EventStatus.Draft,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.Events.Add(ev);
        await db.SaveChangesAsync(cancellationToken);

        return ToResponse(ev);
    }

    public async Task<EventResponse> AddTierAsync(Guid eventId, CreateTierRequest request, CancellationToken cancellationToken)
    {
        var ev = await db.Events.Include(e => e.Tiers).FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken)
            ?? throw new NotFoundException("El evento no existe.");

        if (ev.Status != EventStatus.Draft)
        {
            throw new ConflictException("Solo se pueden agregar niveles de boletos mientras el evento esta en borrador.");
        }

        if (ev.Tiers.Any(t => t.Name.Equals(request.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException("Ya existe un nivel de boletos con ese nombre para este evento.");
        }

        var tier = new TicketTier
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            Name = request.Name.Trim(),
            Price = request.Price,
            Quota = request.Quota,
            ReservedCount = 0,
            Version = 0
        };

        db.TicketTiers.Add(tier);
        await db.SaveChangesAsync(cancellationToken);

        // EF ya hizo el fixup de ev.Tiers al detectar la FK coincidente con el Event rastreado.
        return ToResponse(ev);
    }

    public async Task<EventResponse> PublishAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var ev = await db.Events.Include(e => e.Tiers).FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken)
            ?? throw new NotFoundException("El evento no existe.");

        if (ev.Status != EventStatus.Draft)
        {
            throw new ConflictException("Solo un evento en borrador puede publicarse.");
        }

        if (ev.Tiers.Count == 0)
        {
            throw new ValidationException("El evento necesita al menos un nivel de boletos antes de publicarse.");
        }

        ev.Status = EventStatus.Published;
        await db.SaveChangesAsync(cancellationToken);

        return ToResponse(ev);
    }

    public async Task<EventResponse> CancelAsync(Guid eventId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var ev = await db.Events.Include(e => e.Tiers).FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken)
            ?? throw new NotFoundException("El evento no existe.");

        if (ev.Status is EventStatus.Cancelled or EventStatus.Completed)
        {
            throw new ConflictException("El evento ya esta cancelado o finalizado.");
        }

        var tierIds = ev.Tiers.Select(t => t.Id).ToList();

        var activeReservations = await db.Reservations
            .Where(r => tierIds.Contains(r.TicketTierId) &&
                        (r.Status == ReservationStatus.Held || r.Status == ReservationStatus.Confirmed))
            .ToListAsync(cancellationToken);

        foreach (var reservation in activeReservations)
        {
            reservation.Status = ReservationStatus.Cancelled;
            reservation.CancelledAtUtc = DateTime.UtcNow;
        }

        foreach (var tier in ev.Tiers)
        {
            tier.ReservedCount = 0;
        }

        ev.Status = EventStatus.Cancelled;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToResponse(ev);
    }

    private static EventResponse ToResponse(Event ev) => new()
    {
        Id = ev.Id,
        Name = ev.Name,
        Venue = ev.Venue,
        StartsAtUtc = ev.StartsAtUtc,
        Status = ev.Status.ToString(),
        Tiers = ev.Tiers.Select(t => new TierResponse
        {
            Id = t.Id,
            Name = t.Name,
            Price = t.Price,
            Quota = t.Quota,
            Available = t.Available
        }).ToList()
    };
}
