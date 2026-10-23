using System.Net;
using Npgsql;
using RaspiStorage.Exceptions;

namespace RaspiStorage.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            if (context.RequestAborted.IsCancellationRequested)
            {
                _logger.LogInformation("Request {Method} {Path} aborted by the client", context.Request.Method, context.Request.Path);
                return;
            }

            _logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);

            if (context.Response.HasStarted)
                throw;

            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        var statusCode = ex switch
        {
            NotFoundException => HttpStatusCode.NotFound,
            ConflictException => HttpStatusCode.Conflict,
            QuotaExceededException => HttpStatusCode.BadRequest,
            InvalidNameException => HttpStatusCode.BadRequest,
            PostgresException { SqlState: "22001" } => HttpStatusCode.BadRequest,
            PostgresException { SqlState: "23505" } => HttpStatusCode.Conflict,
            _ => HttpStatusCode.InternalServerError
        };

        var response = ex switch
        {
            NotFoundException or ConflictException or QuotaExceededException or InvalidNameException => ex.Message,
            PostgresException { SqlState: "22001" } => "Value is too long",
            PostgresException { SqlState: "23505" } => "An item with this name already exists in this folder",
            _ => "An unexpected error occurred"
        };

        await Results.Problem(detail: response, statusCode: (int)statusCode).ExecuteAsync(context);
    }
}
