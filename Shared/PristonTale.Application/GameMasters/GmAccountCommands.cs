using System.Text;
using PristonTale.Persistence.Abstractions.GameMasters;
using PristonTale.Persistence.Abstractions.Warehouses;
using PristonTale.Simulation.Characters;
using static PristonTale.Application.GameMasters.GmInformationCommands;
using PristonTale.Contracts.Characters;

namespace PristonTale.Application.GameMasters;

// Commands on stored accounts: moving a character to another account and reading an account's warehouse.
internal static class GmAccountCommands
{
    private const int LineWidth = 70;

    // /insert account character: the character (not in the world) moves to the account's first free slot.
    public static async Task Insert(GmCommandContext context)
    {
        var account = context.Word();
        var character = context.Word();
        var repository = context.Commands.Repository;
        if (account.Length == 0 || character.Length == 0 || repository is null)
        {
            return;
        }

        var moved = context.Commands.Directory.FindByName(character) is null
            && await repository.MoveCharacterAsync(character, account, CharacterSlots.Maximum) == CharacterTransfer.Moved;
        context.Reply(moved ? Format("{0} account has been set to character {1}", account, character) : "Unable to connect to account");
    }

    // /open_warehouse [account]: the stored warehouse of an account (the game master's own without one), read from storage
    // and listed in the chat: its gold, then its items.
    public static async Task OpenWarehouse(GmCommandContext context)
    {
        var account = context.Phrase();
        var accounts = context.Commands.Accounts;
        var warehouses = context.Commands.Warehouses;
        if (accounts is null || warehouses is null)
        {
            return;
        }

        long accountId;
        if (account.Length == 0)
        {
            account = context.Issuer.UserId;
            accountId = context.Issuer.AccountId;
        }
        else if (await accounts.FindByUserIdAsync(account) is { } record)
        {
            accountId = record.Id;
        }
        else
        {
            context.Reply(Format("> No account ( {0} )", account));
            return;
        }

        var storage = WarehouseStateCodec.Restore(await warehouses.FindAsync(accountId), context.Commands.WorldData.Current.Items);
        var items = new List<string>();
        foreach (var item in storage.Grid.Items())
        {
            items.Add(item.Count > 1 ? Format("{0} x{1}", item.DisplayName, item.Count) : item.DisplayName);
        }

        context.Reply(Format("> Warehouse ( {0} ) Gold( {1} ) Items( {2} )", account, storage.Money, items.Count));
        var line = new StringBuilder("> ");
        foreach (var item in items)
        {
            if (line.Length > 2 && line.Length + item.Length + 2 > LineWidth)
            {
                context.Reply(line.ToString());
                line.Clear().Append("> ");
            }

            line.Append(line.Length > 2 ? ", " : "").Append(item);
        }

        if (line.Length > 2)
        {
            context.Reply(line.ToString());
        }
    }
}
