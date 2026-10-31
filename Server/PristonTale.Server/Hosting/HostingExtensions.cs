using PristonTale.Application.Contacts;
using PristonTale.Application.Chat;
using PristonTale.Application.GameMasters;
using PristonTale.Application.Services;
using PristonTale.Application.Settings;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.GameMasters;
using PristonTale.Persistence.Abstractions.Settings;
using PristonTale.Persistence.Abstractions.Warehouses;
using PristonTale.Application.GameLogs;
using PristonTale.Application.Saves;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Server.Net;
using PristonTale.Server.World;
using PristonTale.Persistence.Postgres;
using PristonTale.Persistence.Sqlite;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Loot;

namespace PristonTale.Server.Hosting;

public static class HostingExtensions
{
    public const int DefaultGamePort = 10009;

    public static HostApplicationBuilder AddPristonServer(this HostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<ISessionRegistry, SessionRegistry>();
        builder.Services.AddSingleton<CharacterClassRegistry>();

        builder.Services.AddSingleton<AuthAppService>();
        builder.Services.AddSingleton<CharacterAppService>();
        // A factory prevents DI from selecting the collection constructor with an empty field set.
        builder.Services.AddSingleton(_ => new PristonTale.Simulation.World.WorldCatalog());
        builder.Services.AddSingleton<SessionAppService>();
        builder.Services.AddSingleton<NpcAppService>();
        builder.Services.AddSingleton<ItemUseAppService>();
        builder.Services.AddSingleton<RestartAppService>();
        builder.Services.AddSingleton<WingGateAppService>();
        builder.Services.AddSingleton<QuestAppService>();
        builder.Services.AddSingleton<PersonalShopAppService>();
        builder.Services.AddSingleton<PartyAppService>();
        builder.Services.AddSingleton<TradeAppService>();
        builder.Services.AddSingleton<ContactAppService>();
        builder.Services.AddSingleton<StarterGiftAppService>();

        var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
        if (string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            var databasePath = builder.Configuration["Database:SqlitePath"] ?? Path.Combine("data", "pristontale.db");
            builder.Services.AddPristonSqlite(SqliteDatabasePath.Resolve(databasePath));
        }
        else if (string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = builder.Configuration.GetConnectionString("Postgres")
                ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required for the Postgres provider.");
            builder.Services.AddPristonPostgres(connectionString,
                builder.Configuration.GetValue("Database:MaxPoolSize", PostgresPersistenceServiceCollectionExtensions.DefaultMaxPoolSize));
        }
        else
        {
            throw new InvalidOperationException($"Unsupported database provider '{provider}'. Use Sqlite or Postgres.");
        }
        builder.Services.AddHostedService<DatabaseMigrationService>();
        AddCharacterSaves(builder);
        AddGameLogs(builder);

        builder.Services.AddSingleton<WorldDataAppService>();
        builder.Services.AddSingleton<WorldDataHolder>();
        builder.Services.AddHostedService<WorldDataLoadService>();
        builder.Services.AddSingleton<FieldTerrainCatalog>();
        builder.Services.AddHostedService<FieldTerrainLoadService>();

        var worldOptions = new WorldServerOptions
        {
            MaxPendingCommandsPerPlayer = builder.Configuration.GetValue("World:MaxPendingCommandsPerPlayer",
                WorldServerOptions.DefaultMaxPendingCommandsPerPlayer),
            CheckpointSeconds = builder.Configuration.GetValue("World:CheckpointSeconds", WorldServerOptions.DefaultCheckpointSeconds)
        }.Validated();
        builder.Services.AddSingleton(worldOptions);
        builder.Services.AddSingleton(services => new WorldServer(
            services.GetRequiredService<WorldDataHolder>(),
            services.GetRequiredService<PristonTale.Simulation.World.WorldCatalog>(),
            terrain: services.GetRequiredService<FieldTerrainCatalog>(),
            options: worldOptions,
            saves: services.GetRequiredService<ICharacterSaves>(),
            log: services.GetRequiredService<IGameLog>()));
        builder.Services.AddHostedService<WorldTickService>();
        AddChat(builder);
        builder.Services.AddSingleton(services =>
        {
            var gameMasters = services.GetRequiredService<GameMasterCommands>();
            var settings = new ServerSettingsAppService(services.GetRequiredService<IServerSettingsRepository>(),
                services.GetRequiredService<WorldServer>(), gameMasters, services.GetRequiredService<GameServerControl>(), worldOptions);
            gameMasters.Settings = settings;
            return settings;
        });
        // After the world tick (settings apply on it) and before the game server opens.
        builder.Services.AddHostedService<ServerSettingsService>();
        builder.Services.AddHostedService<GameTcpServer>();
        builder.Services.AddHostedService<PristonTale.Server.World.QuestUpkeepService>();
        builder.Services.AddHostedService<PristonTale.Server.World.StarterGiftService>();

        builder.Services.AddSingleton<IServerStatusReadModel, WorldStatusReadModel>();

        return builder;
    }

    // The save journal lives beside the server executable (Database:SaveJournalPath, relative to it).
    private static void AddCharacterSaves(HostApplicationBuilder builder)
    {
        var journal = Path.Combine(AppContext.BaseDirectory, builder.Configuration["Database:SaveJournalPath"] ?? "save-journal");
        var batchSize = builder.Configuration.GetValue("Database:SaveBatchSize", CharacterSaveQueue.DefaultBatchSize);
        if (batchSize < 1)
        {
            throw new InvalidOperationException("Database:SaveBatchSize must be at least 1.");
        }

        builder.Services.AddSingleton(services =>
        {
            var logger = services.GetRequiredService<ILogger<CharacterSaveQueue>>();
            return new CharacterSaveQueue(services.GetRequiredService<ICharacterRepository>(), new SaveJournal(journal), batchSize)
            {
                Failed = exception => logger.LogError(exception, "A character save batch failed; it is retried")
            };
        });
        builder.Services.AddSingleton<ICharacterSaves>(services => services.GetRequiredService<CharacterSaveQueue>());
        builder.Services.AddHostedService<CharacterSaveService>();
        // Room for the leave saves of every player: once stopping, the store journals what storage refuses after one
        // failed attempt, which a connection timeout (15 s) bounds.
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = ShutdownTimeout);
    }

    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(60);

    private static void AddGameLogs(HostApplicationBuilder builder)
    {
        builder.Services.AddSingleton(services =>
        {
            var logger = services.GetRequiredService<ILogger<GameLogQueue>>();
            return new GameLogQueue(services.GetRequiredService<PristonTale.Persistence.Abstractions.GameLogs.IGameLogRepository>(),
                failed: exception => logger.LogError(exception, "An item transfer log batch failed; it is retried"));
        });
        builder.Services.AddSingleton(services =>
        {
            var logger = services.GetRequiredService<ILogger<GmAuditQueue>>();
            return new GmAuditQueue(services.GetRequiredService<IGameMasterRepository>(),
                failed: exception => logger.LogError(exception, "A game-master audit row failed; it is retried"));
        });
        builder.Services.AddSingleton<IGameLog>(services => services.GetRequiredService<GameLogQueue>());
        builder.Services.AddSingleton(GameLogOptions.From(builder.Configuration));
        builder.Services.AddHostedService<GameLogService>();
    }

    private static void AddChat(HostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<ChatDirectory>();
        builder.Services.AddSingleton<GameServerControl>();
        builder.Services.AddSingleton(services =>
        {
            return new GameMasterCommands(services.GetRequiredService<ChatDirectory>(), services.GetRequiredService<WorldServer>(),
                services.GetRequiredService<WorldDataHolder>(), GameMasterOptions.Default, services.GetRequiredService<GameServerControl>(),
                services.GetRequiredService<IGameMasterRepository>(), services.GetRequiredService<GmAuditQueue>())
            {
                Accounts = services.GetRequiredService<IAccountRepository>(),
                Saves = services.GetRequiredService<ICharacterSaves>(),
                Warehouses = services.GetRequiredService<IWarehouseRepository>()
            };
        });
        builder.Services.AddSingleton(services => new ChatAppService(services.GetRequiredService<ChatDirectory>(),
            services.GetRequiredService<WorldServer>(), services.GetRequiredService<GameMasterCommands>()));
    }
}
