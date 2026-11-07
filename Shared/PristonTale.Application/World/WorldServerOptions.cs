using PristonTale.Simulation.Items.Aging;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Loot;

namespace PristonTale.Application.World;

public sealed record WorldServerOptions
{
    // Etervale's server-wide bonus on .inf experience; the original only had level-banded event bonuses on
    // its experience total, so this is a server setting rather than a ported constant.
    public const int DefaultExperienceMultiplier = 5;

    // A client normally has a few commands waiting for the next tick; the cap keeps one connection from filling
    // the queue shared by every player, which holds the full quota of WorldServer.CommandQueueCapacity / this players.
    public const int DefaultMaxPendingCommandsPerPlayer = 16;

    // A crash loses at most this much of what no operation queued for storage: experience, life, position.
    public const int DefaultCheckpointSeconds = 60;

    public static readonly WorldServerOptions Default = new();

    public int ExperienceMultiplier { get; init; } = DefaultExperienceMultiplier;

    public int ExperienceEventPercent { get; init; } = 100;

    public AgingSettings Aging { get; init; } = AgingSettings.Default;

    public Simulation.Items.Mixing.MixingSettings Mixing { get; init; } = Simulation.Items.Mixing.MixingSettings.Default;

    // Game minutes added to the game clock, to start the server at another hour of the day.
    public int ClockOffsetMinutes { get; init; }

    // Rain over the fields that take it on the original's schedule (WeatherSchedule), every WeatherCycleMinutes game
    // minutes.
    public bool Weather { get; init; } = true;

    public int WeatherCycleMinutes { get; init; } = WeatherSchedule.DefaultCycleMinutes;

    public int MaxPendingCommandsPerPlayer { get; init; } = DefaultMaxPendingCommandsPerPlayer;

    public bool GiveStarterGifts { get; init; } = true;

    // Every playing character is captured for storage once per this many seconds, each on its own tick.
    public int CheckpointSeconds { get; init; } = DefaultCheckpointSeconds;

    // The crystal event: 0 off, 1 from 12:00 to 23:59 server time, 2 all day; crystals then call their event monsters.
    public int CrystalEvent { get; init; }

    // Item events that replace drop rolls with event items (rates per 10000); all off by default.
    public ItemEventRates ItemEvents { get; init; } = ItemEventRates.None;

    public WorldServerOptions Validated()
    {
        if (ExperienceMultiplier < 1)
        {
            throw new InvalidOperationException("World:ExperienceMultiplier must be at least 1.");
        }

        if (MaxPendingCommandsPerPlayer < 1)
        {
            throw new InvalidOperationException("World:MaxPendingCommandsPerPlayer must be at least 1.");
        }

        if (WeatherCycleMinutes < WeatherSchedule.MinimumCycleMinutes)
        {
            throw new InvalidOperationException($"World:WeatherCycleMinutes must be at least {WeatherSchedule.MinimumCycleMinutes}.");
        }

        if (CheckpointSeconds < 1)
        {
            throw new InvalidOperationException("World:CheckpointSeconds must be at least 1.");
        }

        if (CrystalEvent is < 0 or > 2)
        {
            throw new InvalidOperationException("World:CrystalEvent must be 0, 1 or 2.");
        }

        if (ExperienceEventPercent < 0 || Aging is null || Aging.PricePercent < 0 || Aging.FailurePercent < 0 || Aging.DoublePercent < 0)
        {
            throw new InvalidOperationException("Experience event and aging percentages must not be negative.");
        }

        return this;
    }
}

// The authoritative command queue refused a command; the caller drops it like a lost packet.
public sealed class WorldBusyException(string message) : InvalidOperationException(message);

// A commit found the character or a reservation changed after its operation was stored: storage is ahead of memory, so
// the session must reload from it. An error, never a refusal.
public sealed class CommitInvariantException(string message) : Exception(message);
