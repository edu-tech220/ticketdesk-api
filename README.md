# TicketDesk API

API REST para la venta de boletos de eventos con cupo limitado: niveles de boletos (tiers),
reservas temporales ("holds") que expiran solas, confirmación de compra y control de
sobreventa bajo concurrencia real.

## Qué resuelve

Vender boletos con cupo limitado tiene un problema clásico de concurrencia: si dos compradores
intentan reservar el último boleto al mismo tiempo, un sistema ingenuo puede vender el mismo
boleto dos veces. TicketDesk resuelve esto con un flujo de **reserva en dos pasos**:

1. **Hold (reserva temporal):** el comprador aparta `N` boletos de un nivel por un tiempo limitado
   (10 minutos por defecto). El cupo se descuenta de inmediato para que nadie más lo use.
2. **Confirm (confirmación):** el comprador confirma el hold antes de que expire. Si no lo hace,
   un `BackgroundService` libera el cupo automáticamente para que vuelva a estar disponible.

Reglas de negocio que no son un CRUD trivial:

- **Control de sobreventa bajo concurrencia:** dos solicitudes simultáneas sobre el mismo nivel
  de boletos compiten por la misma fila; una gana y la otra se reintenta con datos frescos o
  recibe un 409 si ya no hay cupo. Nunca se vende más de lo disponible.
- **Máquina de estados de la reserva:** `Held → Confirmed | Expired | Cancelled`. Cada transición
  valida quién puede hacerla y en qué condiciones (dueño, admin, tiempo restante).
- **Expiración automática de holds:** un `BackgroundService` recorre las reservas vencidas cada
  30 segundos y libera su cupo sin intervención manual.
- **Límites anti-acaparamiento:** máximo de boletos por solicitud y máximo de boletos activos por
  usuario y nivel, además de un *rate limit* sobre el endpoint de reserva para frenar bots.
- **Ciclo de vida del evento:** un evento nace en `Draft`, solo puede publicarse si tiene al menos
  un nivel de boletos, y al cancelarse libera y cancela en cascada todas sus reservas activas.
- **Paginación y filtrado** en el listado público de eventos.

## Arquitectura

```
src/TicketDesk.Api/
├── Controllers/          Auth, Events, Reservations (finos, delegan a Services)
├── Services/             Reglas de negocio (AuthService, EventService, ReservationService)
├── Domain/
│   ├── Entities/         User, Event, TicketTier, Reservation
│   └── Enums/            UserRole, EventStatus, ReservationStatus
├── Data/                 AppDbContext + migraciones EF Core
├── Dtos/                 Contratos de entrada/salida por recurso
├── Security/             JWT, hashing PBKDF2, usuario actual desde el token
├── BackgroundServices/    HoldExpirationBackgroundService
└── Common/               Excepciones de dominio, middleware -> ProblemDetails, políticas de rate limit
```

Capas: **Controller → Service → DbContext**. Los controladores no contienen lógica de negocio;
las reglas de concurrencia, validación y transición de estados viven en los `Service`, que son la
pieza que se prueba con más detalle.

### Decisiones técnicas

- **Token de concurrencia manejado por la aplicación, no por el motor de datos.** En vez de usar
  `rowversion` de SQL Server (que SQLite no soporta), `TicketTier` y `Reservation` tienen una
  columna `Version` marcada como `IsConcurrencyToken()` que `AppDbContext` incrementa en cada
  `SaveChanges`. El resultado es el mismo control optimista (EF lanza
  `DbUpdateConcurrencyException` si alguien más modificó la fila primero), pero funciona igual en
  SQL Server (producción) y SQLite (pruebas), sin duplicar lógica por proveedor.
- **Reintentos acotados en vez de locks pesimistas.** `ReservationService.CreateHoldAsync` reintenta
  hasta `MaxConcurrencyRetries` veces releyendo el nivel de boletos cuando choca con otra escritura
  concurrente, en lugar de bloquear la fila. Es más simple de razonar y evita interbloqueos para el
  tamaño de este dominio.
- **Expiración por polling, no por SQL Agent/cron externo.** Un `BackgroundService` dentro del
  mismo proceso de la API libera los holds vencidos cada 30 segundos. Para este alcance es más
  simple que coordinar un job externo y no depende de infraestructura adicional.
- **PBKDF2 propio en vez de ASP.NET Core Identity.** Identity trae mucho más de lo que este
  dominio necesita (roles, 2FA, UI). Un hasher PBKDF2 + JWT manual cubre el caso de uso con menos
  piezas que entender.
- **`ProblemDetails` uniforme vía middleware.** Las excepciones de dominio
  (`NotFoundException`, `ConflictException`, `ValidationException`, `ForbiddenException`,
  `UnauthorizedException`) se mapean a códigos HTTP y a un cuerpo `application/problem+json`
  consistente en `ExceptionHandlingMiddleware`, para no repetir `try/catch` en cada controlador.

## Cómo correrlo

### Con Docker Compose (API + SQL Server)

```bash
docker compose up --build
```

La API queda disponible en `http://localhost:8080`, con Swagger en `/swagger` (ambiente
`Development`). Al arrancar aplica las migraciones pendientes y crea un usuario administrador de
arranque (`AdminSeed:Email` / `AdminSeed:Password`, configurables por variables de entorno; valores
de ejemplo únicamente, cámbialos antes de usarlos en un entorno real).

### Local, sin Docker

Requiere .NET 8 SDK y una instancia de SQL Server accesible (ajusta
`ConnectionStrings:DefaultConnection` en `appsettings.Development.json` o por variable de entorno).

```bash
dotnet restore
dotnet run --project src/TicketDesk.Api
```

## Cómo probarlo

Las pruebas no requieren SQL Server ni Docker: usan SQLite (archivo temporal) y
`WebApplicationFactory` para levantar la API completa en memoria.

```bash
dotnet test
```

Incluye:

- **Pruebas unitarias** de `ReservationService` (holds, confirmación, cancelación, expiración) y
  `EventService` (ciclo de vida del evento), incluyendo una prueba que lanza **20 solicitudes
  concurrentes** contra un nivel con cupo de 5 y verifica que nunca se reservan más de 5 boletos.
- **Pruebas unitarias** de `AuthService`, el hasher PBKDF2 y la generación de JWT.
- **Pruebas de integración** end-to-end sobre la API real (`WebApplicationFactory`): flujo
  completo de crear evento → publicar → reservar → confirmar → cancelar evento, además de casos
  de autorización (401/403) y una prueba que confirma que el *rate limit* del endpoint de reservas
  responde `429`.

## Flujo típico de la API

1. `POST /api/auth/register` — alta de cliente.
2. `POST /api/auth/login` — devuelve un JWT (el usuario administrador de arranque también inicia
   sesión aquí).
3. `POST /api/events` *(Admin)* — crea un evento en estado `Draft`.
4. `POST /api/events/{id}/tiers` *(Admin)* — agrega niveles de boletos (nombre, precio, cupo).
5. `POST /api/events/{id}/publish` *(Admin)* — publica el evento (requiere al menos un nivel).
6. `GET /api/events` — listado público paginado, filtrable por estado.
7. `POST /api/reservations` *(autenticado)* — aparta boletos de un nivel (hold, sujeto a rate limit).
8. `POST /api/reservations/{id}/confirm` *(dueño)* — confirma la compra antes de que expire.
9. `POST /api/reservations/{id}/cancel` *(dueño del hold, o Admin si ya está confirmada)*.
10. `GET /api/reservations/mine` — reservas del usuario autenticado, paginadas.
11. `POST /api/events/{id}/cancel` *(Admin)* — cancela el evento y libera todo el cupo reservado.

## Stack

C# / .NET 8 · ASP.NET Core Web API · Entity Framework Core · SQL Server (SQLite en pruebas) ·
JWT Bearer · PBKDF2 · Rate limiting · xUnit · WebApplicationFactory · Docker · Docker Compose ·
GitHub Actions · Swagger / OpenAPI.
