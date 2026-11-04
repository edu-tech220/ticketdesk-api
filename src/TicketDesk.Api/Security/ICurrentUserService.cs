using TicketDesk.Api.Domain.Enums;

namespace TicketDesk.Api.Security;

public interface ICurrentUserService
{
    Guid UserId { get; }
    UserRole Role { get; }
}
