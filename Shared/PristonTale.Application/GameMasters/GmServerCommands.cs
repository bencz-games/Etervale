using PristonTale.Application.Chat;
using static PristonTale.Application.GameMasters.GmInformationCommands;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.World;
using PristonTale.Simulation.World;

namespace PristonTale.Application.GameMasters;

// Server-wide commands: notices, the player limit, the shutdown, the refused-login list and the weather.
internal static class GmServerCommands
{
    // "/>text" tells every player "Notice> text" now; "/>>text" sets the standing notice every player in the world
    // receives once with the next heartbeat.
    public static Task Notice(GmCommandContext context)
    {
        var line = context.Line;
        if (line.Length > 2 && line[2] == '>')
        {
            context.Commands.Directory.SetNotice(line[3..]);
            context.Note("standing notice set");
        }
        else
        {
            ServerNotices.Broadcast(context.Commands.Directory, line[2..]);
            context.Note("notice sent");
        }

        return Task.CompletedTask;
    }

    // The wind a game master's rain blows with: a breeze, or a gale in a storm.
    private const byte GmWind = 64;
    private const byte GmStormWind = 192;
    private const byte GmWindHeading = 32;

    // "/weather clear|overcast|rain|storm [density 1-255]" sets the weather of every field that takes rain until
    // "/weather auto" gives it back to the schedule; "/weather" alone tells the weather now.
    public static async Task Weather(GmCommandContext context)
    {
        var word = context.Word().ToLowerInvariant();
        var density = context.Word();
        var intensity = (byte)Math.Clamp(ChatCommandText.StartsWithDigit(density) ? ChatCommandText.ToNumber(density) : WeatherSchedule.HeavyRain, 1, 255);
        WeatherState? weather = word switch
        {
            "clear" => WeatherState.Clear,
            "overcast" => new WeatherState(WeatherKind.Overcast, 0, GmWindHeading, WeatherSchedule.OvercastWind(GmWind)),
            "rain" => new WeatherState(WeatherKind.Rain, intensity, GmWindHeading, GmWind),
            "storm" => new WeatherState(WeatherKind.Storm, intensity, GmWindHeading, GmStormWind),
            _ => null
        };
        if (word.Length > 0 && weather is null && word != "auto")
        {
            context.Reply("> /weather clear|overcast|rain|storm [1-255] | auto");
            return;
        }

        var world = context.Commands.World;
        var now = word.Length == 0 ? await world.WeatherNowAsync(context.Issuer.EntityId) : await world.SetWeatherAsync(context.Issuer.EntityId, weather);
        context.Reply(Format("> Weather {0} {1} ({2})", now.Kind, now.Intensity, word is "" ? "now" : word == "auto" ? "schedule" : "set"));
    }

    public static Task UserLimit(GmCommandContext context)
    {
        var word = context.Word();
        var server = context.Commands.Server;
        if (word.Length > 0)
        {
            server.UserLimit = Math.Max(0, ChatCommandText.ToNumber(word));
            context.Reply(Format("Maximum users have been set to {0}", server.UserLimit));
        }
        else
        {
            context.Reply(Format("Current maximum users setting is {0}", server.UserLimit));
        }

        return Task.CompletedTask;
    }

    public static async Task ReloadSettings(GmCommandContext context)
    {
        if (context.Commands.Settings is not { } settings)
        {
            context.Reply("The settings table is not available");
            return;
        }

        var current = await settings.ReloadAsync();
        context.Reply(Format("Settings reloaded ({0}, {1} problems)", current.ServerName, settings.Problems.Count));
        foreach (var problem in settings.Problems.Take(5))
        {
            context.Reply(problem);
        }
    }

    // "/setting name" shows a setting; "/setting name value" writes and applies it.
    public static async Task Setting(GmCommandContext context)
    {
        var name = context.Word();
        var value = context.Rest.Trim();
        if (context.Commands.Settings is not { } settings
            || Settings.ServerSettingCatalog.Find(name) is not { } definition)
        {
            context.Reply(Format("Unknown setting {0}", name));
            return;
        }

        if (value.Length == 0)
        {
            context.Reply(Format("{0} = {1}: {2}", definition.Name, settings.ValueOf(definition), definition.Description));
            return;
        }

        context.Reply(await settings.SetAsync(definition.Name, value)
            ? Format("{0} = {1}", definition.Name, value)
            : Format("{0} refused for {1}", value, definition.Name));
    }

    // Starts the countdown: notices, then a reminder every half minute (every five seconds in the last minute), then every
    // client is told to save and quit and the server stops.
    public static Task Shutdown(GmCommandContext context)
    {
        context.Note(context.Commands.Server.BeginShutdown() ? "shutdown started" : "shutdown already running");
        return Task.CompletedTask;
    }

    public static Task ShowBlocked(GmCommandContext context)
    {
        var filter = context.Word();
        var shown = 0;
        foreach (var blocked in context.Commands.Server.BlockedLogins())
        {
            if (filter.Length == 0 || blocked.Account.Equals(filter, StringComparison.OrdinalIgnoreCase))
            {
                context.Reply(Format("> ID ( {0} ) / ( {1} )Min.", blocked.Account, blocked.RemainingMs / 60_000));
                shown++;
            }
        }

        context.Reply(Format("> Total {0} Users", shown));
        return Task.CompletedTask;
    }

    public static Task ResetBlocked(GmCommandContext context)
    {
        context.Commands.Server.ClearBlockedLogins();
        context.Reply("> Reset BlackUser OK!");
        return Task.CompletedTask;
    }

    public static Task FreeBlocked(GmCommandContext context)
    {
        context.Reply(context.Commands.Server.ReleaseBlockedLogin(context.Word()) ? "> Free BlackUser OK!" : "> Non-Blackuser, Check again.");
        return Task.CompletedTask;
    }
}

// Notices the server sends to everybody: from a game master, or from the server itself (the shutdown countdown).
public static class ServerNotices
{
    public static void Broadcast(ChatDirectory directory, string text) =>
        directory.Broadcast(new ChatLine(ChatKind.Notice, 0, Simulation.Social.ChatRules.Notice(text)));

    // A line that already carries its own prefix (the shutdown countdown).
    public static void BroadcastRaw(ChatDirectory directory, string text) =>
        directory.Broadcast(new ChatLine(ChatKind.Notice, 0, Simulation.Social.ChatRules.ClipToCapacity(text)));
}
