using PristonTale.Application.Sessions;
using PristonTale.Application.World.ItemUses;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Texts;

namespace PristonTale.Application.World;

// A validated item use. A null Candidate is final (a refusal or a replay); otherwise the snapshot is stored before the
// commit applies the use to the live character and the world. Summon names the monster a crystal calls; Money is the
// gold the use takes, applied again at commit.
public sealed record PreparedItemUseOperation(
    PlayerCharacter Original,
    PlayerCharacter? Candidate,
    CharacterSaveSnapshot? Snapshot,
    ItemUseOutcome Outcome,
    ItemUseCommand Command,
    UsableItemRule? Rule = null,
    uint ItemCode = 0,
    long NowSeconds = 0,
    int Roll = 0,
    TeleportArrival? Arrival = null,
    string CatalogKey = "",
    string Summon = "",
    long Money = 0,
    PotionDefinition? Food = null)
    : PreparedCharacterOperation<ItemUseOutcome>(Original, Candidate, Snapshot, Outcome);

// The tick's view of one use: who, where, the catalogs, the real clock and the rolls. Pick is 0..9999 for the tables of
// crystals, boxes and puzzles; NowMs is the real clock in milliseconds for the crystal delay.
internal sealed record ItemUseContext(PlayerSession Session, WorldPlayer Player, FieldInstance Field, WorldCatalog World, ItemCatalog Items,
    long NowSeconds, long NowMs, int Roll, int Pick, ISimRng Rng, bool CrystalEvent);

// What a use adds to its candidate beyond the character part: a refusal, the crystal's monster, the gold taken, the
// food eaten and the line the player reads.
internal readonly record struct ItemUseExtra(ItemUseStatus Status, string Summon = "", long Money = 0, PotionDefinition? Food = null, GameText Message = default)
{
    public static ItemUseExtra None => new(ItemUseStatus.Used);
}

// Runs on the world tick. The item must be carried (not equipped), usable, and the inventory revision current; the
// item's kind (ItemUseActions) decides the rest. Every outcome of a stored use is recorded with the character for
// retries.
internal static class ItemUseTransactions
{
    public static PreparedItemUseOperation Prepare(ItemUseContext context, ItemUseCommand command)
    {
        var session = context.Session;
        var player = context.Player;
        var original = player.Character;
        if (original.ItemUses.Find(command.OperationId) is { } applied)
        {
            return applied.Command == command
                ? Final(original, command, applied.Outcome with { Message = GameText.Of(GameTextId.AlreadyApplied) })
                : Final(original, command, ItemUseOutcome.Refused(original.InventoryRevision, ItemUseStatus.Conflict));
        }

        if (command.OperationId == Guid.Empty || command.ExpectedRevision != original.InventoryRevision)
        {
            return Final(original, command, ItemUseOutcome.Refused(original.InventoryRevision, ItemUseStatus.Stale));
        }

        var anchor = original.Inventory.Find(command.ItemId);
        var item = anchor >= 0 ? original.Inventory[anchor] : null;
        if (item is null || !UsableItemCatalog.TryGet(item.Definition.CatalogKey, out var rule))
        {
            return Final(original, command, ItemUseOutcome.Refused(original.InventoryRevision, ItemUseStatus.NotUsable));
        }

        var code = item.Definition.Code;
        var action = ItemUseActions.For(rule.Action);
        context.World.TryGetFieldByCode(context.Field.FieldId, out var here);
        var use = new ItemUseRequest(context, command, rule, item, here);
        var status = Check(action, use);
        if (status != ItemUseStatus.Used)
        {
            return Final(original, command, ItemUseOutcome.Refused(original.InventoryRevision, status, code));
        }

        var candidate = original.CloneForInventoryOperation();
        if (action.Consumes && !candidate.Inventory.ConsumeAt(anchor))
        {
            return Final(original, command, ItemUseOutcome.Refused(original.InventoryRevision, ItemUseStatus.NotUsable, code));
        }

        var extra = action.Extend(use, candidate);
        if (extra.Status != ItemUseStatus.Used)
        {
            return Final(original, command, ItemUseOutcome.Refused(original.InventoryRevision, extra.Status, code));
        }

        UsableItemEffects.Apply(candidate, rule, context.NowSeconds, BigHeadMode(context.Roll));
        var message = extra.Message.IsNone ? action.Message(item.Definition) : extra.Message;
        var outcome = new ItemUseOutcome(true, candidate.InventoryRevision, ItemUseStatus.Used, code, message);
        candidate.ItemUses.Record(new AppliedItemUse(command, outcome.Revision, outcome));
        var arrival = action.Arrival(use);
        var fieldId = arrival?.FieldCode ?? context.Field.FieldId;
        var position = arrival?.Position ?? player.Position;
        var snapshot = action.Store(CharacterRecordFactory.Capture(session.Loaded!, candidate, fieldId, position), rule);
        return new PreparedItemUseOperation(original, candidate, snapshot, outcome, command, rule, code, context.NowSeconds, context.Roll, arrival,
            item.Definition.CatalogKey, extra.Summon, extra.Money, extra.Food);
    }

    // The rules every kind shares, then the kind's own.
    private static ItemUseStatus Check(ItemUseAction action, in ItemUseRequest use)
    {
        var player = use.Context.Player;
        if (player.IsDead != action.ForTheDead)
        {
            return player.IsDead ? ItemUseStatus.Dead : ItemUseStatus.NotUsable;
        }

        if (player.Shop is not null || player.Offers(use.Item.InstanceId))
        {
            return ItemUseStatus.Busy;
        }

        if (use.Rule.ForbiddenInVillage && player.InVillage)
        {
            return ItemUseStatus.NotHere;
        }

        return use.Character.Level < use.Rule.MinimumLevel ? ItemUseStatus.LevelTooLow : action.Check(use);
    }

    // The big-head potion draws one of its two looks from the use's roll.
    public static int BigHeadMode(int roll) => 1 + roll / 2 % 2;

    private static PreparedItemUseOperation Final(PlayerCharacter original, ItemUseCommand command, ItemUseOutcome outcome) =>
        new(original, null, null, outcome, command);
}
