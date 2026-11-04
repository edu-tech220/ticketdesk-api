using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TicketDesk.Api.Common;
using TicketDesk.Api.Domain.Enums;
using TicketDesk.Api.Dtos.Common;
using TicketDesk.Api.Dtos.Reservations;
using TicketDesk.Api.Security;
using TicketDesk.Api.Services;

namespace TicketDesk.Api.Controllers;

[ApiController]
[Route("api/reservations")]
[Authorize]
public class ReservationsController(IReservationService reservationService, ICurrentUserService currentUser) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Reservations)]
    [ProducesResponseType(typeof(ReservationResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<ReservationResponse>> CreateHold(CreateHoldRequest request, CancellationToken cancellationToken)
    {
        var result = await reservationService.CreateHoldAsync(currentUser.UserId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("{reservationId:guid}/confirm")]
    [ProducesResponseType(typeof(ReservationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReservationResponse>> Confirm(Guid reservationId, CancellationToken cancellationToken)
    {
        var result = await reservationService.ConfirmAsync(currentUser.UserId, reservationId, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{reservationId:guid}/cancel")]
    [ProducesResponseType(typeof(ReservationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReservationResponse>> Cancel(Guid reservationId, CancellationToken cancellationToken)
    {
        var isAdmin = currentUser.Role == UserRole.Admin;
        var result = await reservationService.CancelAsync(currentUser.UserId, isAdmin, reservationId, cancellationToken);
        return Ok(result);
    }

    [HttpGet("mine")]
    [ProducesResponseType(typeof(PagedResult<ReservationResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ReservationResponse>>> GetMine(
        [FromQuery] int page, [FromQuery] int pageSize, CancellationToken cancellationToken)
    {
        var result = await reservationService.GetMineAsync(
            currentUser.UserId, page == 0 ? 1 : page, pageSize == 0 ? 10 : pageSize, cancellationToken);
        return Ok(result);
    }
}
