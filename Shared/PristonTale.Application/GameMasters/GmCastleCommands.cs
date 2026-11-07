using PristonTale.Application.Chat;
using PristonTale.Application.Castle;
using static PristonTale.Application.GameMasters.GmInformationCommands;

namespace PristonTale.Application.GameMasters;

// Bless Castle commands: start and end a siege, set its end, its owner and the tax, and read the castle and its fountain.
internal static class GmCastleCommands
{
    // /start_castle [minutes]
    public static async Task Start(GmCommandContext context)
    {
        if (Castle(context) is not { } castle)
        {
            return;
        }

        context.Reply(await castle.StartSiegeAsync(ChatCommandText.ToNumber(context.Word())) ? "> Bless Castle siege started" : "> A siege is already running");
    }

    // /end_castle
    public static async Task End(GmCommandContext context)
    {
        if (Castle(context) is { } castle)
        {
            context.Reply(await castle.EndSiegeAsync() ? "> Bless Castle siege ended" : "> No siege is running");
        }
    }

    // /set_castle_time minutes
    public static async Task Time(GmCommandContext context)
    {
        if (Castle(context) is { } castle)
        {
            var minutes = ChatCommandText.ToNumber(context.Word());
            context.Reply(minutes <= 0 ? "> /set_castle_time minutes"
                : await castle.SetSiegeMinutesAsync(minutes) ? Format("> The siege ends in {0} minutes", minutes)
                : "> No siege is running");
        }
    }

    // /set_castle_master [clan]: the issuer's clan without a name; outside a siege.
    public static async Task Master(GmCommandContext context)
    {
        if (Castle(context) is not { } castle)
        {
            return;
        }

        var name = context.Rest;
        var clanId = name.Length == 0 ? castle.Clans.ClanOf(context.Issuer.EntityId) : castle.Clans.IdOf(name);
        context.Reply((clanId == 0 ? CastleOwnerResult.NoClan : await castle.SetOwnerAsync(clanId)) switch
        {
            CastleOwnerResult.Set => Format("> Castle Master Clan [ {0} ]", castle.Clans.NameOf(clanId) ?? ""),
            CastleOwnerResult.DuringSiege => "> Not during a siege",
            CastleOwnerResult.NoClan => "> No such clan",
            _ => GameMasterCommands.StorageFailed
        });
    }

    // /get_castle_info
    public static Task Info(GmCommandContext context)
    {
        if (Castle(context) is { } castle)
        {
            var view = castle.View();
            context.Reply(Format("> BlessCastle ( MasterClan[{0}] Tax[{1}] NextTax[{2}] ) MyClan({3}) Mode({4})", view.OwnerName, view.Tax, view.NextTax,
                castle.Clans.NameOf(castle.Clans.ClanOf(context.Issuer.EntityId)) ?? "", (int)view.Siege));
        }

        return Task.CompletedTask;
    }

    // /get_fountain_info
    public static Task Fountain(GmCommandContext context)
    {
        if (Castle(context) is { } castle)
        {
            context.Reply(Format("> SoulFountain ( Potion[{0}] )", castle.View().FountainPotions));
        }

        return Task.CompletedTask;
    }

    // /reset_fountain_time
    public static async Task ResetFountain(GmCommandContext context)
    {
        if (Castle(context) is { } castle)
        {
            await castle.ResetFountainTimeAsync();
            context.Reply("> Reset SoulFountain");
        }
    }

    // /easy_castle
    public static async Task Easy(GmCommandContext context)
    {
        if (Castle(context) is { } castle)
        {
            context.Reply(await castle.WeakenSiegeAsync() ? "> Every castle defender has one life left" : "> No siege is running");
        }
    }

    // /set_castle_tax: the next tax and the chosen clan skill take effect now.
    public static async Task Tax(GmCommandContext context)
    {
        if (Castle(context) is not { } castle)
        {
            return;
        }

        if (!await castle.ApplyTaxAsync())
        {
            context.Reply(GameMasterCommands.StorageFailed);
            return;
        }

        var view = castle.View();
        context.Reply(Format("> BlessCastle ( MasterClan[{0}] Tax[{1}] NextTax[{2}] )", view.OwnerName, view.Tax, view.NextTax));
    }

    private static CastleAppService? Castle(GmCommandContext context)
    {
        if (context.Commands.Castle is { Available: true } castle)
        {
            return castle;
        }

        context.Reply("> Bless Castle is not available");
        return null;
    }
}
