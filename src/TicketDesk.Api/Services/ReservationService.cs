using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TicketDesk.Api.Common;
using TicketDesk.Api.Data;
using TicketDesk.Api.Domain.Entities;
using TicketDesk.Api.Domain.Enums;
using TicketDesk.Api.Dtos.Common;
using TicketDesk.Api.Dtos.Reservations;

namespace TicketDesk.Api.Services;

/// <summary>
/// Reglas de negocio de reservas de boletos. El control de sobreventa se apoya en un token
/// de concurrencia optimista sobre TicketTier.Version: dos reservas concurrentes sobre el mismo
/// nivel compiten por la misma fila y, si chocan, se reintenta con los datos frescos en vez de
/// arriesgar una condicion de carrera que permita vender mas boletos que el cupo disponible.
/// </summary>
public class ReservationService(AppDbContext db, IOptions<ReservationOptions> options) : IReservationService
{
    private readonly ReservationOptions _options = options.Value;

    public async Task<ReservationResponse> CreateHoldAsync(Guid userId, CreateHoldRequest request, CancellationToken cancellationToken)
    {
        if (request.Quantity < 1 || request.Quantity > _options.MaxQuantityPerHold)
        {
            throw new ValidationException($"La cantidad debe estar entre 1 y {_options.MaxQuantityPerHold} boletos por reserva.");
        }

        for (var attempt = 1; attempt <= _options.MaxConcurrencyRetries; attempt++)
        {
            var tier = await db.TicketTiers.Include(t => t.Event)
                .FirstOrDefaultAsync(t => t.Id == request.TicketTierId, cancellationToken)
                ?? throw new NotFoundException("El nivel de boletos no existe.");

            var ev = tier.Event ?? throw new NotFoundException("El evento no existe.");

            if (ev.Status != EventStatus.Published)
            {
                throw new ConflictException("El evento no esta disponible para reservas.");
            }

            if (ev.StartsAtUtc <= DateTime.UtcNow)
            {
                throw new ConflictException("El evento ya comenzo.");
            }

            var activeQuantityForUser = await db.Reservations
                .Where(r => r.TicketTierId == tier.Id && r.UserId == userId &&
                            (r.Status == ReservationStatus.Held || r.Status == ReservationStatus.Confirmed))
                .SumAsync(r => (int?)r.Quantity, cancellationToken) ?? 0;

            if (activeQuantityForUser + request.Quantity > _options.MaxActiveTicketsPerUserPerTier)
            {
                throw new ConflictException(
                    $"No puedes mantener mas de {_options.MaxActiveTicketsPerUserPerTier} boletos activos de este nivel.");
            }

            if (tier.Available < request.Quantity)
            {
                throw new ConflictException("No hay suficiente disponibilidad para la cantidad solicitada.");
            }

            tier.ReservedCount += request.Quantity;

            var reservation = new Reservation
            {
                Id = Guid.NewGuid(),
                TicketTierId = tier.Id,
                UserId = userId,
                Quantity = request.Quantity,
                Status = ReservationStatus.Held,
                HeldAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(_options.HoldDurationMinutes),
                Version = 0
            };

            db.Reservations.Add(reservation);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return ToResponse(reservation, ev, tier);
            }
            catch (DbUpdateConcurrencyException) when (attempt < _options.MaxConcurrencyRetries)
            {
                db.ChangeTracker.Clear();
            }
        }

        throw new ConflictException("No se pudo completar la reserva por alta demanda, intenta de nuevo.");
    }

    public async Task<ReservationResponse> ConfirmAsync(Guid userId, Guid reservationId, CancellationToken cancellationToken)
    {
        var reservation = await db.Reservations.Include(r => r.TicketTier).ThenInclude(t => t!.Event)
            .FirstOrDefaultAsync(r => r.Id == reservationId, cancellationToken)
            ?? throw new NotFoundException("La reserva no existe.");

        if (reservation.UserId != userId)
        {
            throw new ForbiddenException("No puedes confirmar una reserva de otro usuario.");
        }

        if (reservation.Status != ReservationStatus.Held)
        {
            throw new ConflictException($"La reserva no se puede confirmar porque esta en estado {reservation.Status}.");
        }

        if (reservation.ExpiresAtUtc <= DateTime.UtcNow)
        {
            ReleaseCapacity(reservation);
            reservation.Status = ReservationStatus.Expired;
            await db.SaveChangesAsync(cancellationToken);
            throw new ConflictException("La reserva expiro antes de confirmarse.");
        }

        reservation.Status = ReservationStatus.Confirmed;
        reservation.ConfirmedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return ToResponse(reservation, reservation.TicketTier!.Event!, reservation.TicketTier);
    }

    public async Task<ReservationResponse> CancelAsync(Guid userId, bool isAdmin, Guid reservationId, CancellationToken cancellationToken)
    {
        var reservation = await db.Reservations.Include(r => r.TicketTier).ThenInclude(t => t!.Event)
            .FirstOrDefaultAsync(r => r.Id == reservationId, cancellationToken)
            ?? throw new NotFoundException("La reserva no existe.");

        if (!isAdmin && reservation.UserId != userId)
        {
            throw new ForbiddenException("No puedes cancelar una reserva de otro usuario.");
        }

        switch (reservation.Status)
        {
            case ReservationStatus.Held:
                break;
            case ReservationStatus.Confirmed when isAdmin:
                break;
            case ReservationStatus.Confirmed:
                throw new ForbiddenException("Solo un administrador puede cancelar una reserva ya confirmada.");
            default:
                throw new ConflictException($"La reserva ya esta en estado {reservation.Status} y no se puede cancelar.");
        }

        ReleaseCapacity(reservation);
        reservation.Status = ReservationStatus.Cancelled;
        reservation.CancelledAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return ToResponse(reservation, reservation.TicketTier!.Event!, reservation.TicketTier);
    }

    public async Task<PagedResult<ReservationResponse>> GetMineAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize switch { < 1 => 1, > 50 => 50, _ => pageSize };

        var query = db.Reservations.AsNoTracking()
            .Include(r => r.TicketTier).ThenInclude(t => t!.Event)
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.HeldAtUtc);

        var totalCount = await query.CountAsync(cancellationToken);

        var reservations = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ReservationResponse>
        {
            Items = reservations.Select(r => ToResponse(r, r.TicketTier!.Event!, r.TicketTier!)).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<int> ExpireDueHoldsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var dueReservationIds = await db.Reservations
            .Where(r => r.Status == ReservationStatus.Held && r.ExpiresAtUtc <= now)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var releasedCount = 0;

        foreach (var reservationId in dueReservationIds)
        {
            for (var attempt = 1; attempt <= _options.MaxConcurrencyRetries; attempt++)
            {
                var reservation = await db.Reservations.Include(r => r.TicketTier)
                    .FirstOrDefaultAsync(r => r.Id == reservationId, cancellationToken);

                if (reservation is null || reservation.Status != ReservationStatus.Held || reservation.ExpiresAtUtc > DateTime.UtcNow)
                {
                    break;
                }

                ReleaseCapacity(reservation);
                reservation.Status = ReservationStatus.Expired;

                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                    releasedCount++;
                    break;
                }
                catch (DbUpdateConcurrencyException) when (attempt < _options.MaxConcurrencyRetries)
                {
                    db.ChangeTracker.Clear();
                }
            }
        }

        return releasedCount;
    }

    private static void ReleaseCapacity(Reservation reservation)
    {
        if (reservation.TicketTier is not null)
        {
            reservation.TicketTier.ReservedCount = Math.Max(0, reservation.TicketTier.ReservedCount - reservation.Quantity);
        }
    }

    private static ReservationResponse ToResponse(Reservation reservation, Event ev, TicketTier tier) => new()
    {
        Id = reservation.Id,
        TicketTierId = tier.Id,
        EventId = ev.Id,
        EventName = ev.Name,
        TierName = tier.Name,
        Quantity = reservation.Quantity,
        Status = reservation.Status.ToString(),
        HeldAtUtc = reservation.HeldAtUtc,
        ExpiresAtUtc = reservation.ExpiresAtUtc,
        ConfirmedAtUtc = reservation.ConfirmedAtUtc,
        CancelledAtUtc = reservation.CancelledAtUtc
    };
}
