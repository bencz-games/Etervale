using System.Numerics;
using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

// Runs on the world tick. A quest request is evaluated against a candidate copy of the character; when the journal,
// items, progression or gold changed, the candidate is stored before the commit applies it. Requests that follow a
// dialog are accepted only while the NPC they name still offers that step.
internal static class QuestTransactions
{
    public static QuestNpc DescribeNpc(WorldNpc npc, PlayerCharacter character, int fieldCode)
    {
        var definition = npc.Definition;
        var giver = definition.Find(NpcServiceKind.QuestGiver);
        var script = definition.Find(NpcServiceKind.QuestScript);
        var master = definition.Find(NpcServiceKind.SkillMaster);
        var wing = definition.Find(NpcServiceKind.WarpGate);
        return new QuestNpc(
            giver?.Value ?? 0,
            script?.Value ?? 0,
            script?.Value2 ?? 0,
            master?.Value ?? 0,
            master is not null && QuestClasses.IsHomeTown(character, fieldCode),
            wing is not null && wing.Value <= 1,
            definition.Has(NpcServiceKind.ItemAging));
    }

    public static PreparedQuestOperation Prepare(PlayerSession session, WorldPlayer player, FieldInstance field, QuestCommand command,
        ItemCatalog items, WorldCatalog world, ISimRng rng, long nowUnixMs)
    {
        var original = player.Character;
        var journal = original.Quests;
        if (command.OperationId == Guid.Empty || !Enum.IsDefined(command.Action))
        {
            return Final(original, QuestOutcome.Failed(QuestStatus.Rejected, "invalid quest request", journal.Revision));
        }

        if (journal.Find(command.OperationId) is { } applied)
        {
            return Final(original, applied.Command == command
                ? new QuestOutcome(true, QuestStatus.Ok, "already committed", journal.Revision, null, [])
                : QuestOutcome.Failed(QuestStatus.Conflict, "operation identity conflict", journal.Revision));
        }

        var npc = QuestNpc.None;
        if (command.NpcEntityId != 0)
        {
            if (!field.TryGetNpc(command.NpcEntityId, out var worldNpc) || !NpcServiceTransactions.CanReach(player, worldNpc))
            {
                return Final(original, QuestOutcome.Failed(QuestStatus.OutOfReach, "approach this NPC while alive", journal.Revision));
            }

            npc = DescribeNpc(worldNpc, original, field.FieldId);
        }
        else if (command.Action is QuestAction.Talk or QuestAction.ChangeClass || player.IsDead
            || command.Action is QuestAction.Proceed or QuestAction.Accept or QuestAction.ChooseReward && !OpenedByWorld(journal.Active, command))
        {
            return Final(original, QuestOutcome.Failed(QuestStatus.OutOfReach, "talk to the NPC first", journal.Revision));
        }

        var source = new CatalogQuestItemSource(items);
        if (command.NpcEntityId != 0 && command.Action is QuestAction.Proceed or QuestAction.Accept or QuestAction.ChooseReward
            && !Offered(original, npc, source, nowUnixMs, field.FieldId, command))
        {
            return Final(original, QuestOutcome.Failed(QuestStatus.Stale, "the quest has moved on; talk to the NPC again", journal.Revision));
        }

        var candidate = original.CloneForInventoryOperation();
        var context = new QuestContext(candidate, npc, source, rng, nowUnixMs, field.FieldId);
        var ok = QuestEngine.Run(context, command);
        if (!ok)
        {
            return Final(original, new QuestOutcome(false, context.Status, context.Message, journal.Revision, null, context.Notices)
            {
                ClassChangeOffered = context.ClassChangeOffered
            });
        }

        var changed = candidate.Quests.Revision != journal.Revision || candidate.InventoryRevision != original.InventoryRevision
            || candidate.Tier != original.Tier || candidate.StatPoints != original.StatPoints || candidate.SkillPoints != original.SkillPoints
            || candidate.EliteSkillPoints != original.EliteSkillPoints || candidate.Money != original.Money;
        var outcome = new QuestOutcome(true, QuestStatus.Ok, "", candidate.Quests.Revision, context.Prompt, context.Notices)
        {
            ClassChangeOffered = context.ClassChangeOffered,
            SkillResetOffered = candidate.Quests.SkillResetOffered && !journal.SkillResetOffered,
            Travel = context.Travel,
            Duel = context.Duel
        };
        if (!changed)
        {
            return new PreparedQuestOperation(original, null, null, outcome);
        }

        candidate.Quests.Record(command);
        var (fieldId, position) = Destination(world, context.Travel, field.FieldId, player.Position);
        var snapshot = CharacterRecordFactory.Capture(session.Loaded!, candidate, fieldId, position);
        return new PreparedQuestOperation(original, candidate, snapshot, outcome with { Revision = candidate.Quests.Revision })
        {
            Tier = candidate.Tier,
            StatPoints = candidate.StatPoints - original.StatPoints,
            SkillPoints = candidate.SkillPoints - original.SkillPoints,
            EliteSkillPoints = candidate.EliteSkillPoints - original.EliteSkillPoints,
            MoneyDelta = candidate.Money - original.Money,
            ResetSkills = command.Action == QuestAction.ResetSkills
        };
    }

    // The upkeep: stores the kill's quest drops, grows or expires the quest weapon, clears leftovers and gives the aging
    // copies that reached their goal their level.
    public static PreparedQuestOperation PrepareUpkeep(PlayerSession session, WorldPlayer player, FieldInstance field, ItemCatalog items, ISimRng rng,
        long nowUnixMs)
    {
        var original = player.Character;
        var drops = player.QuestState.TakeDrops();
        var candidate = original.CloneForInventoryOperation();
        var context = new QuestContext(candidate, QuestNpc.None, new CatalogQuestItemSource(items), rng, nowUnixMs, field.FieldId);
        QuestDrops.Upkeep(context, drops);
        candidate.MatureAgedItems();
        var outcome = new QuestOutcome(true, QuestStatus.Ok, "", candidate.Quests.Revision, null, context.Notices);
        if (candidate.Quests.Revision == original.Quests.Revision && candidate.InventoryRevision == original.InventoryRevision)
        {
            return new PreparedQuestOperation(original, null, null, outcome) { Upkeep = true };
        }

        var snapshot = CharacterRecordFactory.Capture(session.Loaded!, candidate, field.FieldId, player.Position);
        return new PreparedQuestOperation(original, candidate, snapshot, outcome)
        {
            Tier = candidate.Tier,
            Drops = drops,
            Upkeep = true
        };
    }

    // A step the world opened without an NPC: the victory dialog of a duel and the rank-up it offers.
    private static bool OpenedByWorld(ActiveQuest active, QuestCommand command) =>
        active.Code == command.Code && active.State == command.State && active.ArenaWon
        && (command.Code == QuestCode.FuryOfPhantom && command.Action == QuestAction.Proceed
            || command.Code == QuestCode.RankUpMasterTrial && command.Action == QuestAction.Accept);

    // Whether the NPC offers the step a follow-up answers: talking to it (or pressing its class-change button for the
    // start of a rank-up) on a scratch copy shows that step's dialog.
    private static bool Offered(PlayerCharacter original, QuestNpc npc, IQuestItemSource items, long nowUnixMs, int fieldCode,
        QuestCommand command)
    {
        if (OpenedByWorld(original.Quests.Active, command))
        {
            return true;
        }

        var scratch = original.CloneForInventoryOperation();
        var context = new QuestContext(scratch, npc, items, new SeededSimRng(0), nowUnixMs, fieldCode);
        if (QuestCodes.IsRankUp(command.Code) && command.State == 0 && command.Action == QuestAction.Accept)
        {
            RankUpRules.ChangeClass(context);
        }
        else
        {
            QuestEngine.Talk(context);
        }

        var prompt = context.Prompt;
        if (prompt is null || prompt.Code != command.Code)
        {
            return false;
        }

        // The reward list and the reward view share the step: a choice may be browsed and changed before it is claimed.
        return prompt.State == command.State || command.Code == QuestCode.FuryOfPhantom && command.State is 12 or 13 && prompt.State is 12 or 13;
    }

    private static (int FieldId, Vector3 Position) Destination(WorldCatalog world, QuestTravel? travel, int fieldId, Vector3 position)
    {
        if (travel is null || !world.TryGetFieldByCode(travel.FieldCode, out var destination))
        {
            return (fieldId, position);
        }

        return (destination.FieldCode, TravelPoint(destination, travel));
    }

    public static Vector3 TravelPoint(FieldDef destination, QuestTravel travel)
    {
        if (travel.X != 0 || travel.Z != 0)
        {
            return new Vector3(travel.X, 0f, travel.Z);
        }

        var start = destination.Arrival;
        return new Vector3(start.X, 0f, start.Z);
    }

    private static PreparedQuestOperation Final(PlayerCharacter original, QuestOutcome outcome) => new(original, null, null, outcome);
}
