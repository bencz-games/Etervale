using Dapper;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PristonTale.Persistence.Abstractions;

namespace PristonTale.Persistence.Postgres;

public static class PostgresPersistenceServiceCollectionExtensions
{
    // Below PostgreSQL's default max_connections (100, three reserved), so a full pool never meets "too many clients".
    public const int DefaultMaxPoolSize = 20;

    // maxPoolSize replaces the connection string's Maximum Pool Size.
    public static IServiceCollection AddPristonPostgres(this IServiceCollection services, string connectionString, int maxPoolSize = DefaultMaxPoolSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPoolSize, 1);
        connectionString = new NpgsqlConnectionStringBuilder(connectionString) { MaxPoolSize = maxPoolSize }.ConnectionString;

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        SqlMapper.AddTypeHandler(new JsonTextNpgsqlHandler());

        services.AddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());
        services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();

        return services.AddPristonRepositories(connectionString, static rb => rb.AddPostgres());
    }
}
