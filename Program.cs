using System.Net;
using System.Security.Cryptography.X509Certificates;
using RaspiStorage.Data;
using RaspiStorage.Middleware;
using RaspiStorage.Models;
using RaspiStorage.Services;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File("logs/app.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();
    builder.Services.AddControllers();
    builder.Services.AddCertificateForwarding(options =>
    {
        options.CertificateHeader = "X-Client-Cert";

        options.HeaderConverter = (headerValue) =>
        {
            X509Certificate2? clientCertificate = null;

            if (!string.IsNullOrWhiteSpace(headerValue))
            {
                clientCertificate = X509Certificate2.CreateFromPem(
                    WebUtility.UrlDecode(headerValue));
            }

            return clientCertificate!;
        };
    });

    builder.Services.AddSingleton<IDbConnectionHandler>(_ =>
        new DbConnectionHandler(
            builder.Configuration.GetConnectionString("DefaultConnection")!
        ));

    builder.Services.Configure<StorageOptions>(
        builder.Configuration.GetSection("Storage")
    );

    builder.Services.AddScoped<IUserRepository, UserRepository>();
    builder.Services.AddScoped<IFolderRepository, FolderRepository>();
    builder.Services.AddScoped<IFileRepository, FileRepository>();
    builder.Services.AddScoped<IActivityRepository, ActivityRepository>();
    builder.Services.AddScoped<IFileService, FileService>();
    builder.Services.AddScoped<IFolderService, FolderService>();
    builder.Services.AddScoped<IActivityService, ActivityService>();

    var app = builder.Build();

    app.UseSerilogRequestLogging();
    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.UseCertificateForwarding();
    app.UseMiddleware<UserResolutionMiddleware>();

    app.MapControllers();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application failed to start");
    Log.CloseAndFlush();
    return 1;
}

Log.CloseAndFlush();
return 0;
