using TicketDesk.Api.Domain.Entities;

namespace TicketDesk.Api.Security;

public interface ITokenService
{
    string GenerateToken(User user);
}
