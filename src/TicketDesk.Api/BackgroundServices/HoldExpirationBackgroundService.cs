using TicketDesk.Api.Services;

namespace TicketDesk.Api.BackgroundServices;

/// <summary>
/// Recorre periodicamente las reservas en estado Held cuyo ExpiresAtUtc ya paso y libera
/// su cupo, para que boletos abandonados por un comprador vuelvan a estar disponibles
/// sin intervencion manual.
/// </summary>
public class HoldExpirationBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<HoldExpirationBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var reservationService = scope.ServiceProvider.GetRequiredService<IReservationService>();
                var released = await reservationService.ExpireDueHoldsAsync(stoppingToken);

                if (released > 0)
                {
                    logger.LogInformation("Se liberaron {Count} reservas vencidas.", released);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error liberando reservas vencidas.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // el servicio se esta deteniendo
            }
        }
    }
}
