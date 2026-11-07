using System.Globalization;
using PristonTale.Application.Chat;

namespace PristonTale.Application.GameMasters;

// Commands that only report: the server clock, timers, the economy, potions, connections and players.
internal static class GmInformationCommands
{
    private const string NotOnline = "Currently not online, or cannot be found";

    public static Task LocalTime(GmCommandContext context)
    {
        var now = context.Commands.World.LocalNow;
        context.Reply(Format("TIME [{0}]> {1}/{2}/{3} ( {4}:{5}:{6} )", context.Commands.Options.ServerCode, now.Year, now.Month, now.Day, now.Hour,
            now.Minute, now.Second));
        return Task.CompletedTask;
    }

    public static Task BossTime(GmCommandContext context)
    {
        context.Reply(Format("{0}> Boss Time ( {1} min.)", context.Commands.Options.ServerCode, context.Commands.World.BossMinute));
        return Task.CompletedTask;
    }

    // Only while a force orb runs on the game master.
    public static async Task ForceOrb(GmCommandContext context)
    {
        if (await Inspect(context, context.Issuer) is { ForceOrb: { } orb })
        {
            context.Reply(Format("{0}> Force Orb - [{1}] ( {2} sec )( {3} dmg )", context.Commands.Options.ServerCode, orb.Icon, orb.Seconds, orb.Damage));
        }
    }

    // Only while Third Eyes or an experience potion runs on the game master; this server sells no package codes, so the
    // package number is 0.
    public static async Task PremiumItems(GmCommandContext context)
    {
        if (await Inspect(context, context.Issuer) is { } report && (report.ThirdEyesSeconds > 0 || report.ExperienceUpSeconds > 0))
        {
            context.Reply(Format("{0}> Prime Item - Package[{1}] DropItem( {2} min ) ExpUp( {3} min )", context.Commands.Options.ServerCode, 0,
                report.ThirdEyesSeconds / 60, report.ExperienceUpSeconds / 60));
        }
    }

    // Gold that left the economy since the start: mixing, aging, shops, the three potion kinds, force orbs, teleports and
    // skill training, then the total; the second line counts millions.
    public static Task Currency(GmCommandContext context)
    {
        var ledger = context.Commands.World.Currency;
        context.Reply(CurrencyLine(">currency: ", ledger, 1));
        context.Reply(CurrencyLine(">currency[M]: ", ledger, 1_000_000));
        return Task.CompletedTask;
    }

    // Carried potions of a player (the game master without a name), by kind and grade.
    public static async Task Potions(GmCommandContext context)
    {
        var name = context.Word();
        var target = name.Length == 0 ? context.Issuer : context.Commands.Directory.FindByName(name);
        if (target is null || await Inspect(context, target) is not { } report)
        {
            return;
        }

        var potions = report.Potions;
        context.Reply(Format(">PL({0} {1} {2} {3}) PM({4} {5} {6} {7}) PS({8} {9} {10} {11})", potions[0, 0], potions[0, 1], potions[0, 2],
            potions[0, 3], potions[1, 0], potions[1, 1], potions[1, 2], potions[1, 3], potions[2, 0], potions[2, 1], potions[2, 2], potions[2, 3]));
    }

    // Packets of the game master's own connection; a failed or out-of-order packet closes a connection on this server, so
    // the failure counters stay zero.
    public static Task NetLog(GmCommandContext context)
    {
        var link = context.Issuer.Link;
        context.Reply(Format("> Rcv({0}) Snd({1}) ScoopRcv({2}) ScoopFail({3}) SndFail({4}) RcvFail({5})", link.PacketsReceived, link.PacketsSent,
            link.PacketsReceived, 0, 0, 0));
        return Task.CompletedTask;
    }

    public static async Task Users(GmCommandContext context)
    {
        var (players, monsters) = await context.Commands.World.CountsAsync(context.Issuer.EntityId);
        context.Reply(Format("Users {0} / Active Monsters {1}", players, monsters));
    }

    public static async Task Sockets(GmCommandContext context)
    {
        var (_, monsters) = await context.Commands.World.CountsAsync(context.Issuer.EntityId);
        context.Reply(Format("Open ({0}) / Active ({1})", context.Commands.Server.OpenConnections, monsters));
    }

    public static async Task AreaUsers(GmCommandContext context)
    {
        var (field, players) = await context.Commands.World.FieldPlayersAsync(context.Issuer.EntityId);
        context.Reply(Format("> Area({0}) Users({1})", field, players));
    }

    public static Task UserByEntityId(GmCommandContext context)
    {
        var word = context.Word();
        if (word.Length == 0)
        {
            return Task.CompletedTask;
        }

        var entityId = ChatCommandText.ToEntityId(word);
        Describe(context, entityId != 0 && context.Commands.Directory.TryGet(entityId, out var member) ? member : null);
        return Task.CompletedTask;
    }

    public static Task UserByAccount(GmCommandContext context)
    {
        var word = context.Word();
        if (word.Length > 0)
        {
            Describe(context, context.Commands.Directory.FindByUserId(word));
        }

        return Task.CompletedTask;
    }

    public static Task UserByName(GmCommandContext context)
    {
        var word = context.Word();
        if (word.Length > 0)
        {
            Describe(context, context.Commands.Directory.FindByName(word));
        }

        return Task.CompletedTask;
    }

    // The gold the game master entered the world with, and the gold the server holds for the character now.
    public static async Task Gold(GmCommandContext context)
    {
        if (await Inspect(context, context.Issuer) is { } report)
        {
            context.Reply(Format("> Server Money ( {0} )( {1} )", context.Issuer.StartingMoney, report.Money));
        }
    }

    // The damage range and critical chance the server computes for the game master.
    public static async Task DamageLimits(GmCommandContext context)
    {
        if (await Inspect(context, context.Issuer) is { } report)
        {
            context.Reply(Format("Limit Damage ( {0} {1} {2} )", report.DamageMin, report.DamageMax, report.Critical));
        }
    }

    // The session the game master is logged in with; this server runs no billing, so the billing fields are empty.
    public static Task Session(GmCommandContext context)
    {
        context.Reply(Format("> Guid( {0} ) GpCode( {1} ) PCRno( {2} ) GpType( {3} )", context.Issuer.Session.SessionToken, "", 0, 0));
        return Task.CompletedTask;
    }

    public static Task<GmCharacterReport?> Inspect(GmCommandContext context, ChatMember target) =>
        context.Commands.World.InspectAsync(context.Issuer.EntityId, target.EntityId);

    public static string Format(string format, params object[] values) => string.Format(CultureInfo.InvariantCulture, format, values);

    private static void Describe(GmCommandContext context, ChatMember? member)
    {
        if (member is null)
        {
            context.Reply(NotOnline);
            return;
        }

        var minutes = (context.Commands.NowMs() - member.JoinedMs) / 60_000;
        context.Reply(Format("Account( {0} ) Name( {1} ) IP( {2} ) Connected( {3} mins )", member.UserId, member.Name, member.Link.RemoteAddress, minutes));
    }

    private static string CurrencyLine(string prefix, CurrencyLedger ledger, long unit) => prefix + Format(
        "Mx({0}) Ag({1}) Sh({2}) Po({3} {4} {5}) Fo({6}) Wp({7}) Sk({8} ) Bt({9}) Tt({10})",
        ledger[CurrencySink.Mixing] / unit, ledger[CurrencySink.Aging] / unit, ledger[CurrencySink.Shop] / unit, ledger[CurrencySink.LifePotions] / unit,
        ledger[CurrencySink.ManaPotions] / unit, ledger[CurrencySink.StaminaPotions] / unit, ledger[CurrencySink.ForceOrbs] / unit,
        ledger[CurrencySink.Teleports] / unit, ledger[CurrencySink.SkillTraining] / unit, ledger[CurrencySink.BellatraTickets] / unit, ledger.Total / unit);
}
