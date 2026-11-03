using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Contacts;
using PristonTale.Persistence.Abstractions.GameMasters;
using PristonTale.Persistence.Abstractions.GameLogs;
using PristonTale.Persistence.Abstractions.Settings;
using PristonTale.Persistence.Abstractions.Warehouses;
using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Persistence.Migrations;
using PristonTale.Persistence.Repositories;

namespace PristonTale.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    // The repositories and the migration runner every provider shares; the provider registers its connection factory and
    // adds its migration processor through provider.
    public static IServiceCollection AddPristonRepositories(this IServiceCollection services, string connectionString,
        Func<IMigrationRunnerBuilder, IMigrationRunnerBuilder> provider)
    {
        services.AddSingleton<IAccountRepository, DapperAccountRepository>();
        services.AddSingleton<ICharacterRepository, DapperCharacterRepository>();
        services.AddSingleton<IWorldDataRepository, DapperWorldDataRepository>();
        services.AddSingleton<IWarehouseRepository, DapperWarehouseRepository>();
        services.AddSingleton<IContactRepository, DapperContactRepository>();
        services.AddSingleton<IGameMasterRepository, DapperGameMasterRepository>();
        services.AddSingleton<IGameLogRepository, DapperGameLogRepository>();
        services.AddSingleton<IServerSettingsRepository, DapperServerSettingsRepository>();
        services.AddSingleton<IDatabaseMigrator, FluentMigratorDatabaseMigrator>();

        services.AddFluentMigratorCore()
            .ConfigureRunner(rb => provider(rb)
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(M013Baseline).Assembly).For.Migrations());

        return services;
    }
}
