using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TicketDesk.Api.Common;
using TicketDesk.Api.Data;
using TicketDesk.Api.Domain.Entities;
using TicketDesk.Api.Domain.Enums;
using TicketDesk.Api.Dtos.Auth;
using TicketDesk.Api.Security;

namespace TicketDesk.Api.Services;

public class AuthService(
    AppDbContext db,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IOptions<JwtOptions> jwtOptions) : IAuthService
{
    private readonly JwtOptions _jwtOptions = jwtOptions.Value;

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var alreadyExists = await db.Users.AnyAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (alreadyExists)
        {
            throw new ConflictException("Ya existe una cuenta registrada con este correo.");
        }

        var (hash, salt) = passwordHasher.Hash(request.Password);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = hash,
            PasswordSalt = salt,
            Role = UserRole.Customer,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        return BuildResponse(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash, user.PasswordSalt))
        {
            throw new UnauthorizedException("Correo o contraseña invalidos.");
        }

        return BuildResponse(user);
    }

    private AuthResponse BuildResponse(User user)
    {
        return new AuthResponse
        {
            Token = tokenService.GenerateToken(user),
            Email = user.Email,
            DisplayName = user.DisplayName,
            Role = user.Role.ToString(),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(_jwtOptions.ExpirationMinutes)
        };
    }
}
