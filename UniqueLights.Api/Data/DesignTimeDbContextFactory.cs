using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using UniqueLights.Api.Extensions;

namespace UniqueLights.Api.Data;

/// <summary>
/// Used by the dotnet-ef tooling (migrations add / script). It does not need a reachable database:
/// when no connection string is configured, a placeholder is used so migrations can be generated offline.
/// For "database update", set ConnectionStrings__DefaultConnection (env, .env or user-secrets).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string PlaceholderConnection = "Host=localhost;Port=5432;Database=unique_lights;Username=postgres;Password=postgres";

    public AppDbContext CreateDbContext(string[] args)
    {
        DotEnvLoader.Load(Directory.GetCurrentDirectory());

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<DesignTimeDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = PlaceholderConnection;
        }

        var builder = new DbContextOptionsBuilder<AppDbContext>();
        builder.UseNpgsql(connectionString, o => o.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();

        return new AppDbContext(builder.Options);
    }
}
