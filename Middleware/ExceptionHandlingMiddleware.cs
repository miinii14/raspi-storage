using System.Diagnostics;
using Npgsql;
using RaspiStorage.Exceptions;

namespace RaspiStorage.Middleware;

public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("Request {Method} {Path} aborted by the client",
                context.Request.Method, context.Request.Path);
        }
        catch (Exception ex)
        {
            var (status, detail, code) = Map(ex);

            if (ex is ApiException)
                logger.LogInformation("{Code} for {Method} {Path}: {Message}",
                    code, context.Request.Method, context.Request.Path, ex.Message);
            else if (status >= 500)
                logger.LogError(ex, "Unhandled exception for {Method} {Path}",
                    context.Request.Method, context.Request.Path);
            else
                logger.LogWarning(ex, "{Code} for {Method} {Path}",
                    code, context.Request.Method, context.Request.Path);

            if (context.Response.HasStarted)
                throw;

            await Results.Problem(
                detail: detail,
                statusCode: status,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = code,
                    ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier
                }).ExecuteAsync(context);
        }
    }

    private static (int Status, string Detail, string Code) Map(Exception ex) => ex switch
    {
        ApiException app => (app.StatusCode, app.Message, app.ErrorCode),
        PostgresException pg => MapPostgres(pg),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred", "internal_error")
    };

    private static (int, string, string) MapPostgres(PostgresException pg) => pg.SqlState switch
    {
        PostgresErrorCodes.StringDataRightTruncation =>
            (StatusCodes.Status400BadRequest, "Value is too long", "value_too_long"),
        PostgresErrorCodes.UniqueViolation =>
            (StatusCodes.Status409Conflict, "An item with this name already exists", "duplicate"),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred", "internal_error")
    };
}