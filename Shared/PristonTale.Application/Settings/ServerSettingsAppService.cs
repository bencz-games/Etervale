using PristonTale.Application.GameMasters;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Settings;

namespace PristonTale.Application.Settings;

public sealed class ServerSettingsAppService(
    IServerSettingsRepository repository,
    WorldServer world,
    GameMasterCommands gameMasters,
    IServerOperations server,
    WorldServerOptions hostWorld)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _seeded;
    private ServerSettings? _current;
    private IReadOnlyDictionary<string, string> _rows = new Dictionary<string, string>();

    public ServerSettings Current => Volatile.Read(ref _current) ?? ServerSettings.Default;

    public IReadOnlyList<string> Problems { get; private set; } = [];

    public async Task<ServerSettings> ReloadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!_seeded)
            {
                await repository.SeedAsync(ServerSettingCatalog.Seeds, ct);
                _seeded = true;
            }

            var rows = await repository.LoadAsync(ct);
            _rows = rows;
            var problems = new List<string>();
            var settings = ServerSettingCatalog.Read(rows, hostWorld, problems);
            Problems = problems;
            var previous = _current;
            if (previous is null || previous.World != settings.World)
            {
                await world.ApplyOptionsAsync(settings.World);
            }

            gameMasters.Options = settings.GameMasters;
            server.ServerName = settings.ServerName;
            if (previous is null || previous.MaxUsers != settings.MaxUsers)
            {
                server.UserLimit = settings.MaxUsers;
            }

            Volatile.Write(ref _current, settings);
            return settings;
        }
        finally
        {
            _gate.Release();
        }
    }

    public string ValueOf(ServerSettingDefinition definition) => _rows.TryGetValue(definition.Name, out var value) ? value : definition.Default;

    public async Task<bool> SetAsync(string name, string value, CancellationToken ct = default)
    {
        var definition = ServerSettingCatalog.Find(name);
        if (definition is null || definition.Apply(Current, value.Trim()) is null || !await repository.SetAsync(definition.Name, value.Trim(), ct))
        {
            return false;
        }

        await ReloadAsync(ct);
        return true;
    }
}
