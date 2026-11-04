using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TicketDesk.Api.Dtos.Auth;
using TicketDesk.Api.Dtos.Common;
using TicketDesk.Api.Dtos.Events;
using TicketDesk.Api.Dtos.Reservations;
using TicketDesk.Api.Dtos.Tiers;
using Xunit;

namespace TicketDesk.Tests.Integration;

/// <summary>
/// Cada caso de prueba crea su propia WebApplicationFactory (y por lo tanto su propia base SQLite
/// de archivo) en vez de compartir una via IClassFixture, para que las pruebas no interfieran
/// entre si con datos de otras ejecuciones (eventos, reservas, usuarios ya registrados, etc.).
/// </summary>
public class TicketDeskEndToEndTests : IAsyncLifetime
{
    private TicketDeskWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new TicketDeskWebApplicationFactory();
        await _factory.InitializeDatabaseAsync();
        await _factory.SeedAdminUserAsync("admin@ticketdesk.local", "AdminPass123!");
        _client = _factory.CreateClient();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        return body!.Token;
    }

    private async Task<string> RegisterCustomerAsync(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email,
            DisplayName = "Cliente de prueba",
            Password = "ClaveCliente123!"
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        return body!.Token;
    }

    private void UseToken(string token) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    [Fact]
    public async Task FullLifecycle_FromEventCreationToTicketConfirmation_Works()
    {
        var adminToken = await LoginAsync("admin@ticketdesk.local", "AdminPass123!");
        UseToken(adminToken);

        var createEventResponse = await _client.PostAsJsonAsync("/api/events", new CreateEventRequest
        {
            Name = "Festival de Jazz",
            Venue = "Teatro Municipal",
            StartsAtUtc = DateTime.UtcNow.AddDays(45)
        });
        Assert.Equal(HttpStatusCode.Created, createEventResponse.StatusCode);
        var createdEvent = await createEventResponse.Content.ReadFromJsonAsync<EventResponse>();

        var addTierResponse = await _client.PostAsJsonAsync($"/api/events/{createdEvent!.Id}/tiers", new CreateTierRequest
        {
            Name = "General",
            Price = 350,
            Quota = 3
        });
        Assert.Equal(HttpStatusCode.OK, addTierResponse.StatusCode);
        var eventWithTier = await addTierResponse.Content.ReadFromJsonAsync<EventResponse>();
        var tierId = eventWithTier!.Tiers.Single().Id;

        var publishResponse = await _client.PostAsync($"/api/events/{createdEvent.Id}/publish", null);
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);

        var customerToken = await RegisterCustomerAsync("cliente1@correo.com");
        UseToken(customerToken);

        var listResponse = await _client.GetAsync("/api/events?status=Published");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<EventResponse>>();
        Assert.Contains(page!.Items, e => e.Id == createdEvent.Id);

        var holdResponse = await _client.PostAsJsonAsync("/api/reservations", new CreateHoldRequest { TicketTierId = tierId, Quantity = 2 });
        Assert.Equal(HttpStatusCode.Created, holdResponse.StatusCode);
        var hold = await holdResponse.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.Equal("Held", hold!.Status);

        var confirmResponse = await _client.PostAsync($"/api/reservations/{hold.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        var confirmed = await confirmResponse.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.Equal("Confirmed", confirmed!.Status);

        var mineResponse = await _client.GetAsync("/api/reservations/mine");
        Assert.Equal(HttpStatusCode.OK, mineResponse.StatusCode);
        var mine = await mineResponse.Content.ReadFromJsonAsync<PagedResult<ReservationResponse>>();
        Assert.Contains(mine!.Items, r => r.Id == hold.Id);

        // Solo queda 1 boleto disponible (3 de cupo - 2 confirmados); pedir 2 debe fallar con 409.
        var secondCustomerToken = await RegisterCustomerAsync("cliente2@correo.com");
        UseToken(secondCustomerToken);
        var oversellResponse = await _client.PostAsJsonAsync("/api/reservations", new CreateHoldRequest { TicketTierId = tierId, Quantity = 2 });
        Assert.Equal(HttpStatusCode.Conflict, oversellResponse.StatusCode);

        UseToken(adminToken);
        var cancelResponse = await _client.PostAsync($"/api/events/{createdEvent.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        var cancelledEvent = await cancelResponse.Content.ReadFromJsonAsync<EventResponse>();
        Assert.Equal("Cancelled", cancelledEvent!.Status);
        Assert.Equal(3, cancelledEvent.Tiers.Single().Available);
    }

    [Fact]
    public async Task CreateEvent_WithoutToken_ReturnsUnauthorized()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.PostAsJsonAsync("/api/events", new CreateEventRequest
        {
            Name = "Evento sin token",
            Venue = "Lugar",
            StartsAtUtc = DateTime.UtcNow.AddDays(5)
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_AsCustomer_ReturnsForbidden()
    {
        var customerToken = await RegisterCustomerAsync("noadmin@correo.com");
        UseToken(customerToken);

        var response = await _client.PostAsJsonAsync("/api/events", new CreateEventRequest
        {
            Name = "Evento prohibido",
            Venue = "Lugar",
            StartsAtUtc = DateTime.UtcNow.AddDays(5)
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateHold_ExceedingRateLimit_ReturnsTooManyRequests()
    {
        var adminToken = await LoginAsync("admin@ticketdesk.local", "AdminPass123!");
        UseToken(adminToken);

        var createEventResponse = await _client.PostAsJsonAsync("/api/events", new CreateEventRequest
        {
            Name = "Evento de carga",
            Venue = "Arena",
            StartsAtUtc = DateTime.UtcNow.AddDays(60)
        });
        var createdEvent = await createEventResponse.Content.ReadFromJsonAsync<EventResponse>();

        var addTierResponse = await _client.PostAsJsonAsync($"/api/events/{createdEvent!.Id}/tiers", new CreateTierRequest
        {
            Name = "General",
            Price = 100,
            Quota = 1000
        });
        var eventWithTier = await addTierResponse.Content.ReadFromJsonAsync<EventResponse>();
        var tierId = eventWithTier!.Tiers.Single().Id;
        await _client.PostAsync($"/api/events/{createdEvent.Id}/publish", null);

        var customerToken = await RegisterCustomerAsync("ratelimited@correo.com");
        UseToken(customerToken);

        var tasks = Enumerable.Range(0, 15)
            .Select(_ => _client.PostAsJsonAsync("/api/reservations", new CreateHoldRequest { TicketTierId = tierId, Quantity = 1 }));
        var responses = await Task.WhenAll(tasks);

        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.TooManyRequests);
    }
}
