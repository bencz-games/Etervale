using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Ai;
using PristonTale.Contracts.Texts;

namespace PristonTale.Simulation.Quests;

// What a clicked NPC does for quests: the quest trigger code of its class field, the server quest script, the
// class-change group of a skill master (0 when it trains no class change) and whether it is the master of the player's
// home town, and the wing exchange and aging windows quests use.
public readonly record struct QuestNpc(
    int GiverCode = 0,
    int ScriptCode = 0,
    int ScriptParam = 0,
    int ClassChangeGroup = 0,
    bool HomeMaster = false,
    bool WingExchange = false,
    bool ItemAging = false)
{
    public static readonly QuestNpc None = new();
}

// Creates the items quests hand out from the world's item catalog.
public interface IQuestItemSource
{
    bool TryCreate(uint code, int count, uint jobSpecMask, out ItemInstance item);

    // The quest variant of a code (its own name and stats), or the regular item when the code has no variant.
    bool TryCreateQuestVariant(uint code, uint jobSpecMask, out ItemInstance item);
}

public sealed class CatalogQuestItemSource(ItemCatalog catalog) : IQuestItemSource
{
    public bool TryCreate(uint code, int count, uint jobSpecMask, out ItemInstance item)
    {
        item = null;
        if (!catalog.TryGet(code, out var definition) || count < 1 || count > definition.MaxStack)
        {
            return false;
        }

        item = new ItemInstance(definition, count);
        Specialize(item, jobSpecMask);
        return true;
    }

    public bool TryCreateQuestVariant(uint code, uint jobSpecMask, out ItemInstance item)
    {
        if (!catalog.TryGetQuestVariant(code, out var definition))
        {
            return TryCreate(code, 1, jobSpecMask, out item);
        }

        item = new ItemInstance(definition);
        Specialize(item, jobSpecMask);
        return true;
    }

    // A class reward is made out for the receiver's class.
    private static void Specialize(ItemInstance item, uint jobSpecMask)
    {
        if (jobSpecMask != 0)
        {
            item.Roll = item.Roll with { SpecJob = jobSpecMask };
        }
    }
}

// One quest request evaluated against a character: on the world tick, against a candidate copy when the request may
// change durable state. Rules write the dialog to show, the notices to post and any travel the quest asks for.
public sealed class QuestContext(PlayerCharacter character, QuestNpc npc, IQuestItemSource items, ISimRng rng, long nowUnixMs, int fieldCode)
{
    private readonly List<QuestNotice> _notices = [];

    public PlayerCharacter Character { get; } = character;
    public QuestJournal Journal => Character.Quests;
    public QuestNpc Npc { get; } = npc;
    public IQuestItemSource Items { get; } = items;
    public ISimRng Rng { get; } = rng;
    public long NowUnixMs { get; } = nowUnixMs;
    public int FieldCode { get; } = fieldCode;

    public QuestPrompt Prompt { get; set; }
    public bool ClassChangeOffered { get; set; }
    public QuestTravel Travel { get; set; }
    public QuestArenaDuel Duel { get; set; }

    // The player leaves the arena for the home town and the duel ends (a fight won or lost on the world tick).
    public bool Retreat { get; set; }
    public QuestStatus Status { get; private set; } = QuestStatus.Ok;
    public GameText Message { get; private set; }
    public IReadOnlyList<QuestNotice> Notices => _notices;

    public bool Tempskron => QuestClasses.IsTempskron(Character);

    public void Notice(QuestNoticeKind kind, GameText text) => _notices.Add(new QuestNotice(kind, text));

    // Shows a dialog unless one is already shown for this request; the first quest an NPC answers with wins.
    public void Show(QuestPrompt prompt)
    {
        Prompt ??= prompt;
    }

    public bool Fail(QuestStatus status, GameText message)
    {
        Status = status;
        Message = message;
        return false;
    }

    // Another quest blocks a talk: the quest-in-progress message is posted when the NPC has no dialog to show.
    public bool Blocked { get; private set; }

    public void Block() => Blocked = true;

    // Another quest blocks an explicit request.
    public bool Busy() => Fail(QuestStatus.InProgress, QuestTexts.QuestInProgress);

    // Why the last grant of this request did not fit.
    public QuestGrantCheck GrantRefusal { get; set; }
}
