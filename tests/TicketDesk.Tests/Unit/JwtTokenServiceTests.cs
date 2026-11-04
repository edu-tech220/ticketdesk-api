using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using TicketDesk.Api.Domain.Entities;
using TicketDesk.Api.Domain.Enums;
using TicketDesk.Api.Security;
using Xunit;

namespace TicketDesk.Tests.Unit;

public class JwtTokenServiceTests
{
    [Fact]
    public void GenerateToken_IncludesUserIdEmailAndRoleClaims()
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "TicketDesk.Tests",
            Audience = "TicketDesk.Tests.Clients",
            SigningKey = "unit-test-signing-key-at-least-32-chars-long",
            ExpirationMinutes = 15
        });
        var service = new JwtTokenService(options);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "admin@ticketdesk.local",
            DisplayName = "Admin",
            Role = UserRole.Admin
        };

        var token = service.GenerateToken(user);
        var parsed = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal(user.Id.ToString(), parsed.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal(user.Email, parsed.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.Equal("Admin", parsed.Claims.Single(c => c.Type == ClaimTypes.Role).Value);
    }
}
