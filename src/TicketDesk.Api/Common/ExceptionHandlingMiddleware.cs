using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace TicketDesk.Api.Common;

public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var (statusCode, title) = MapException(ex);

            if (statusCode == HttpStatusCode.InternalServerError)
            {
                logger.LogError(ex, "Error no controlado procesando {Method} {Path}", context.Request.Method, context.Request.Path);
            }

            var problemDetails = new ProblemDetails
            {
                Status = (int)statusCode,
                Title = title,
                Detail = ex.Message,
                Instance = context.Request.Path
            };

            context.Response.StatusCode = (int)statusCode;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(problemDetails);
        }
    }

    private static (HttpStatusCode StatusCode, string Title) MapException(Exception ex) => ex switch
    {
        NotFoundException => (HttpStatusCode.NotFound, "Recurso no encontrado"),
        ConflictException => (HttpStatusCode.Conflict, "Conflicto de estado"),
        ValidationException => (HttpStatusCode.UnprocessableEntity, "Solicitud invalida"),
        ForbiddenException => (HttpStatusCode.Forbidden, "Acceso denegado"),
        UnauthorizedException => (HttpStatusCode.Unauthorized, "No autorizado"),
        DbUpdateConcurrencyException => (HttpStatusCode.Conflict, "La disponibilidad cambio, intenta de nuevo"),
        _ => (HttpStatusCode.InternalServerError, "Error interno del servidor")
    };
}
