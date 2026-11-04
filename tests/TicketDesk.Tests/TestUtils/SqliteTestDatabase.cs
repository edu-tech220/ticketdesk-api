using Microsoft.EntityFrameworkCore;
using TicketDesk.Api.Data;

namespace TicketDesk.Tests.TestUtils;

/// <summary>
/// Base de datos SQLite respaldada por archivo temporal (no en memoria) para que varios
/// AppDbContext independientes puedan abrir su propia conexion y competir de verdad por
/// las mismas filas, tal como lo harian solicitudes HTTP concurrentes contra SQL Server.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly string _dbFilePath;

    public SqliteTestDatabase()
    {
        _dbFilePath = Path.Combine(Path.GetTempPath(), $"ticketdesk-tests-{Guid.NewGuid():N}.db");
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbFilePath};Default Timeout=30")
            .Options;
        return new AppDbContext(options);
    }

    public void Dispose()
    {
        if (File.Exists(_dbFilePath))
        {
            File.Delete(_dbFilePath);
        }
    }
}
