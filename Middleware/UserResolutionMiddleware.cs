using System.Security.Cryptography.X509Certificates;
using RaspiStorage.Data;

namespace RaspiStorage.Middleware;

public class UserResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public UserResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IUserRepository userRepository)
    {
        var clientCertificate = context.Connection.ClientCertificate;

        if (clientCertificate == null)
        {
            await Results.Problem(detail: "Missing client certificate", statusCode: 401).ExecuteAsync(context);
            return;
        }

        var cn = clientCertificate.GetNameInfo(X509NameType.SimpleName, false);

        if (string.IsNullOrEmpty(cn))
        {
            await Results.Problem(detail: "Missing client certificate CN", statusCode: 401).ExecuteAsync(context);
            return;
        }

        var user = await userRepository.GetByUsernameAsync(cn);

        if (user is null)
        {
            await Results.Problem(detail: $"User '{cn}' not found", statusCode: 401).ExecuteAsync(context);
            return;
        }

        context.Items["CurrentUser"] = user;
        await _next(context);
    }
}
