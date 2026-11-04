using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketDesk.Api.Domain.Enums;
using TicketDesk.Api.Dtos.Common;
using TicketDesk.Api.Dtos.Events;
using TicketDesk.Api.Dtos.Tiers;
using TicketDesk.Api.Security;
using TicketDesk.Api.Services;

namespace TicketDesk.Api.Controllers;

[ApiController]
[Route("api/events")]
public class EventsController(IEventService eventService, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResult<EventResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<EventResponse>>> GetPaged(
        [FromQuery] EventQueryParameters query, CancellationToken cancellationToken)
    {
        var result = await eventService.GetPagedAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{eventId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(EventResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<EventResponse>> GetById(Guid eventId, CancellationToken cancellationToken)
    {
        var result = await eventService.GetByIdAsync(eventId, cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Admin))]
    [ProducesResponseType(typeof(EventResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<EventResponse>> Create(CreateEventRequest request, CancellationToken cancellationToken)
    {
        var result = await eventService.CreateAsync(request, currentUser.UserId, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { eventId = result.Id }, result);
    }

    [HttpPost("{eventId:guid}/tiers")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    [ProducesResponseType(typeof(EventResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<EventResponse>> AddTier(Guid eventId, CreateTierRequest request, CancellationToken cancellationToken)
    {
        var result = await eventService.AddTierAsync(eventId, request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{eventId:guid}/publish")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    [ProducesResponseType(typeof(EventResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<EventResponse>> Publish(Guid eventId, CancellationToken cancellationToken)
    {
        var result = await eventService.PublishAsync(eventId, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{eventId:guid}/cancel")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    [ProducesResponseType(typeof(EventResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<EventResponse>> Cancel(Guid eventId, CancellationToken cancellationToken)
    {
        var result = await eventService.CancelAsync(eventId, cancellationToken);
        return Ok(result);
    }
}
