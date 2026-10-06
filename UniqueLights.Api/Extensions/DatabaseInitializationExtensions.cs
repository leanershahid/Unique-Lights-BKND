using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UniqueLights.Api.Configurations;
using UniqueLights.Api.Data;

namespace UniqueLights.Api.Extensions;

public static class DatabaseInitializationExtensions
{
    /// <summary>
    /// Applies pending migrations when Database:MigrateOnStartup is true (defaults to true in Development only),
    /// then runs the idempotent seeder when Seed:Enabled is true.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        var databaseOptions = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var seedOptions = app.Services.GetRequiredService<IOptions<SeedOptions>>().Value;
        var migrate = databaseOptions.MigrateOnStartup ?? app.Environment.IsDevelopment();

        if (!migrate && !seedOptions.Enabled) return;

        await using var scope = app.Services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitialization");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (migrate)
        {
            var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
            if (pending.Count > 0)
            {
                logger.LogInformation("Applying {Count} pending migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
                await db.Database.MigrateAsync();
            }
            else
            {
                logger.LogInformation("Database schema is up to date");
            }
        }

        if (seedOptions.Enabled)
        {
            await scope.ServiceProvider.GetRequiredService<DbSeeder>().SeedAsync();
        }
    }
}
