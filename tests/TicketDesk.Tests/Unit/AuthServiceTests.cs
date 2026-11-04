using Microsoft.Extensions.Options;
using TicketDesk.Api.Common;
using TicketDesk.Api.Dtos.Auth;
using TicketDesk.Api.Security;
using TicketDesk.Api.Services;
using TicketDesk.Tests.TestUtils;
using Xunit;

namespace TicketDesk.Tests.Unit;

public class AuthServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly JwtOptions _jwtOptions = new()
    {
        Issuer = "TicketDesk.Tests",
        Audience = "TicketDesk.Tests.Clients",
        SigningKey = "unit-test-signing-key-at-least-32-chars-long",
        ExpirationMinutes = 30
    };

    private AuthService CreateService() =>
        new(_database.CreateContext(), new Pbkdf2PasswordHasher(), new JwtTokenService(Options.Create(_jwtOptions)), Options.Create(_jwtOptions));

    [Fact]
    public async Task RegisterAsync_CreatesCustomer_AndReturnsToken()
    {
        var service = CreateService();

        var response = await service.RegisterAsync(new RegisterRequest
        {
            Email = "nuevo@correo.com",
            DisplayName = "Nuevo Usuario",
            Password = "SuperSecreta123!"
        }, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(response.Token));
        Assert.Equal("Customer", response.Role);
    }

    [Fact]
    public async Task RegisterAsync_Throws_WhenEmailAlreadyExists()
    {
        var service = CreateService();
        var request = new RegisterRequest { Email = "dup@correo.com", DisplayName = "Uno", Password = "SuperSecreta123!" };

        await service.RegisterAsync(request, CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() => service.RegisterAsync(
            new RegisterRequest { Email = "DUP@correo.com", DisplayName = "Dos", Password = "OtraClave123!" },
            CancellationToken.None));
    }

    [Fact]
    public async Task LoginAsync_Succeeds_WithCorrectCredentials()
    {
        var service = CreateService();
        await service.RegisterAsync(new RegisterRequest { Email = "login@correo.com", DisplayName = "Login", Password = "ClaveValida123!" }, CancellationToken.None);

        var response = await service.LoginAsync(new LoginRequest { Email = "login@correo.com", Password = "ClaveValida123!" }, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(response.Token));
    }

    [Fact]
    public async Task LoginAsync_Throws_WithWrongPassword()
    {
        var service = CreateService();
        await service.RegisterAsync(new RegisterRequest { Email = "wrong@correo.com", DisplayName = "Wrong", Password = "ClaveValida123!" }, CancellationToken.None);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.LoginAsync(new LoginRequest { Email = "wrong@correo.com", Password = "ClaveIncorrecta!" }, CancellationToken.None));
    }

    [Fact]
    public async Task LoginAsync_Throws_WhenUserDoesNotExist()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.LoginAsync(new LoginRequest { Email = "noexiste@correo.com", Password = "Cualquiera123!" }, CancellationToken.None));
    }

    public void Dispose() => _database.Dispose();
}
