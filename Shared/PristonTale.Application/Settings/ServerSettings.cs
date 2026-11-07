using PristonTale.Application.GameMasters;
using PristonTale.Application.World;

namespace PristonTale.Application.Settings;

public sealed record ServerSettings
{
    public const string DefaultServerName = "Etervale";
    public const int DefaultReloadMinutes = 5;

    public static readonly ServerSettings Default = new();

    public string ServerName { get; init; } = DefaultServerName;

    // A game master's /user_limit lasts until this setting changes.
    public int MaxUsers { get; init; }

    public int ReloadMinutes { get; init; } = DefaultReloadMinutes;

    public WorldServerOptions World { get; init; } = WorldServerOptions.Default;

    public GameMasterOptions GameMasters { get; init; } = GameMasterOptions.Default;

    public PristonTale.Application.Castle.CastleOptions Castle { get; init; } = PristonTale.Application.Castle.CastleOptions.Default;
}
