using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>
/// Brings the database schema up to date at startup, so a deploy needs no manual SQL.
///
/// - A Postgres advisory lock makes sure only one instance migrates at a time
///   (EF Core 8 has no lock of its own).
/// - A database created before the Postgres baseline (tables exist, but the
///   InitialPostgres migration isn't recorded) is baselined first, exactly like
///   docs/migrations/prod-upgrade.sql, so EF doesn't try to create existing tables.
/// - Any failure is rethrown: the app must not start on a half-migrated schema.
///   On Render the failed deploy then leaves the previous version running.
///
/// Turn it off with the setting Database__AutoMigrate=false.
/// </summary>
public static class DatabaseMigrator
{
    private const string BaselineMigration = "20261003181758_InitialPostgres";
    private const string BaselineProductVersion = "8.0.22";

    // Any fixed number; it only has to be the same for every instance of this app.
    private const long LockKey = 0x4D514D_4D494752; // "MQM MIGR"

    public static async Task MigrateAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Keep one connection open so the session-level lock covers the whole migration.
        await db.Database.OpenConnectionAsync();
        try
        {
            logger.LogInformation("Database migration: waiting for the migration lock");
            await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_lock({LockKey})");
            try
            {
                await BaselineLegacyDatabaseAsync(db, logger);

                var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
                if (pending.Count == 0)
                {
                    logger.LogInformation("Database migration: schema is up to date");
                    return;
                }

                logger.LogInformation("Database migration: applying {Count} migration(s): {Migrations}",
                    pending.Count, string.Join(", ", pending));
                await db.Database.MigrateAsync();
                logger.LogInformation("Database migration: done");
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_unlock({LockKey})");
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Production predates the Postgres migrations: its tables were created by hand, so the
    /// baseline must be recorded as applied, not run. A brand-new empty database has no
    /// "Branches" table and gets the full baseline from MigrateAsync instead.
    /// </summary>
    private static async Task BaselineLegacyDatabaseAsync(ApplicationDbContext db, ILogger logger)
    {
        var hasTables = await ScalarAsync(db,
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = 'Branches')");
        if (!hasTables) return;

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" character varying(150) NOT NULL,
                "ProductVersion" character varying(32) NOT NULL,
                CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
            )
            """);

        var baselined = await ScalarAsync(db,
            $"SELECT EXISTS (SELECT 1 FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '{BaselineMigration}')");
        if (baselined) return;

        logger.LogWarning("Database migration: existing tables without the Postgres baseline; recording {Migration} as applied",
            BaselineMigration);

        await db.Database.ExecuteSqlRawAsync($"""
            DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" IN (
                '20260104225112_InitialCreate',
                '20260104230849_Admin',
                '20260104235347_AddCurrencyToBranch',
                '20260105222447_AddMultiLanguageAndThemeSupport');
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ('{BaselineMigration}', '{BaselineProductVersion}')
            ON CONFLICT ("MigrationId") DO NOTHING;
            """);
    }

    private static async Task<bool> ScalarAsync(ApplicationDbContext db, string sql)
    {
        var command = db.Database.GetDbConnection().CreateCommand();
        await using (command)
        {
            command.CommandText = sql;
            return (bool)(await command.ExecuteScalarAsync())!;
        }
    }
}
