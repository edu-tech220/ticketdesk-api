using TicketDesk.Api.Domain.Enums;

namespace TicketDesk.Api.Dtos.Events;

public class EventQueryParameters
{
    public EventStatus? Status { get; set; }

    private int _page = 1;
    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    private int _pageSize = 10;
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch { < 1 => 1, > 50 => 50, _ => value };
    }
}
