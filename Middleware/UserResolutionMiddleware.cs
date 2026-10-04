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
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Missing client certificate");
            return;
        }

        var cn = clientCertificate.GetNameInfo(X509NameType.SimpleName, false);

        if (string.IsNullOrEmpty(cn))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Missing client certificate CN");
            return;
        }

        var user = await userRepository.GetByUsernameAsync(cn);

        if (user is null)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync($"User '{cn}' not found");
            return;
        }

        context.Items["CurrentUser"] = user;
        await _next(context);
    }
}
