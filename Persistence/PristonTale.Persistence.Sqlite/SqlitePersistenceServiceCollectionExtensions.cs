using FluentMigrator.Runner;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Persistence.Abstractions;

namespace PristonTale.Persistence.Sqlite;

public static class SqlitePersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPristonSqlite(this IServiceCollection services, string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        SqliteTypeMapping.EnsureRegistered();

        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
        }.ToString();

        services.AddSingleton<IDbConnectionFactory>(new SqliteConnectionFactory(databasePath));
        return services.AddPristonRepositories(connectionString, static rb => rb.AddSQLite());
    }
}
