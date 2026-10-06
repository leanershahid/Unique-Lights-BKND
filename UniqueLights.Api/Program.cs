using Microsoft.AspNetCore.HttpOverrides;
using Serilog;
using UniqueLights.Api.Extensions;
using UniqueLights.Api.Middleware;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    // Local development convenience: load ./.env (never committed) into environment variables.
    DotEnvLoader.Load(Directory.GetCurrentDirectory());

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            path: Path.Combine(context.HostingEnvironment.ContentRootPath, "logs", "unique-lights-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            fileSizeLimitBytes: 50 * 1024 * 1024,
            rollOnFileSizeLimit: true));

    builder.Configuration.ValidateRequiredConfiguration();

    builder.Services
        .AddAppOptions(builder.Configuration)
        .AddPersistence(builder.Configuration, builder.Environment)
        .AddAppAuthentication(builder.Configuration)
        .AddAppServices()
        .AddApiControllers()
        .AddAppCors(builder.Configuration)
        .AddApiRateLimiting();

    if (builder.Environment.IsDevelopment())
    {
        builder.Services.AddAppSwagger();
    }

    // Behind a reverse proxy (Render, Fly, Nginx...) set ForwardedHeaders:Enabled=true so client IPs
    // (rate limiting, refresh-token audit) and the request scheme are correct.
    var useForwardedHeaders = builder.Configuration.GetValue("ForwardedHeaders:Enabled", false);
    if (useForwardedHeaders)
    {
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });
    }

    var app = builder.Build();

    app.UseMiddleware<ExceptionHandlingMiddleware>();
    if (useForwardedHeaders) app.UseForwardedHeaders();
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Unique Lights API v1");
            options.DocumentTitle = "Unique Lights API";
        });
    }

    app.UseRouting();
    app.UseCors(ServiceCollectionExtensions.CorsPolicy);
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();

    app.MapHealthChecks("/health");
    app.MapControllers();

    await app.InitializeDatabaseAsync();
    await app.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Unique Lights API failed to start");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
