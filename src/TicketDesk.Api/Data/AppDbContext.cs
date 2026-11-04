using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using TicketDesk.Api.Domain.Entities;

namespace TicketDesk.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    /// <summary>
    /// Version es un token de concurrencia manejado por la aplicacion (no por el motor de datos),
    /// para que el control optimista funcione igual en SQL Server y SQLite (pruebas).
    /// EF compara el valor original cargado contra la fila en base de datos al hacer SaveChanges;
    /// si otra transaccion ya la modifico, lanza DbUpdateConcurrencyException.
    /// </summary>
    private void BumpConcurrencyTokens()
    {
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State == EntityState.Modified))
        {
            var versionProperty = entry.Properties.FirstOrDefault(p => p.Metadata.Name == nameof(TicketTier.Version));
            if (versionProperty is not null)
            {
                versionProperty.CurrentValue = (int)versionProperty.OriginalValue! + 1;
            }
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        BumpConcurrencyTokens();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        BumpConcurrencyTokens();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<TicketTier> TicketTiers => Set<TicketTier>();
    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.HasIndex(u => u.Email).IsUnique();
            entity.Property(u => u.Email).IsRequired().HasMaxLength(256);
            entity.Property(u => u.DisplayName).IsRequired().HasMaxLength(128);
            entity.Property(u => u.PasswordHash).IsRequired();
            entity.Property(u => u.PasswordSalt).IsRequired();
        });

        modelBuilder.Entity<Event>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Venue).IsRequired().HasMaxLength(200);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.StartsAtUtc);
            entity.HasMany(e => e.Tiers)
                .WithOne(t => t.Event)
                .HasForeignKey(t => t.EventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TicketTier>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Name).IsRequired().HasMaxLength(100);
            entity.Property(t => t.Price).HasPrecision(10, 2);
            entity.Property(t => t.Version).IsConcurrencyToken();
            entity.Ignore(t => t.Available);
        });

        modelBuilder.Entity<Reservation>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Version).IsConcurrencyToken();
            entity.HasIndex(r => new { r.Status, r.ExpiresAtUtc });
            entity.HasIndex(r => r.UserId);
            entity.HasOne(r => r.TicketTier)
                .WithMany()
                .HasForeignKey(r => r.TicketTierId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
