using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TicketDesk.Api.Data;
using TicketDesk.Api.Domain.Entities;
using TicketDesk.Api.Domain.Enums;
using TicketDesk.Api.Security;

namespace TicketDesk.Tests.Integration;

/// <summary>
/// Levanta la API completa en memoria con WebApplicationFactory, reemplazando SQL Server
/// por SQLite (archivo temporal) para que las pruebas de integracion corran sin
/// dependencias externas.
/// </summary>
public class TicketDeskWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbFilePath = Path.Combine(Path.GetTempPath(), $"ticketdesk-it-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite($"Data Source={_dbFilePath}"));
        });

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "TicketDesk.IntegrationTests",
                ["Jwt:Audience"] = "TicketDesk.IntegrationTests.Clients",
                ["Jwt:SigningKey"] = "integration-test-signing-key-at-least-32-chars",
                ["Jwt:ExpirationMinutes"] = "30",
                ["AdminSeed:Email"] = "admin@ticketdesk.local",
                ["AdminSeed:Password"] = "AdminPass123!",
                ["Reservations:HoldDurationMinutes"] = "10",
                ["Reservations:MaxQuantityPerHold"] = "6",
                ["Reservations:MaxActiveTicketsPerUserPerTier"] = "6",
                ["Reservations:MaxConcurrencyRetries"] = "3"
            });
        });
    }

    public async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task<string> SeedAdminUserAsync(string email, string password)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var (hash, salt) = hasher.Hash(password);

        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Email = email.ToLowerInvariant(),
            DisplayName = "Admin de pruebas",
            PasswordHash = hash,
            PasswordSalt = salt,
            Role = UserRole.Admin,
            CreatedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        return email;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (File.Exists(_dbFilePath))
        {
            File.Delete(_dbFilePath);
        }
    }
}
