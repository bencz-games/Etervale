using System.Text.Json;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.Characters;

namespace PristonTale.Persistence.Abstractions.Characters;

public sealed record SavedQuest(
    QuestCode Code,
    int State,
    int Target,
    int Kills,
    int KillsNeeded,
    int Variant,
    QuestTimerKind Timer,
    long TimerMs,
    int StartLevel,
    Guid WeaponId,
    Guid RingId,
    long WeaponCreatedUnixMs,
    int WeaponStage,
    int WeaponKillsLeft,
    int WeaponLifeBonus,
    int WeaponManaRegenTenths,
    bool ArenaWon,
    int Flags = 0);

public sealed record SavedQuestOperation(Guid OperationId, ulong NpcEntityId, QuestAction Action, QuestCode Code, int State, int Choice, long Revision);

// The characters.quests column: the running quest, the completion logs, the finished history and the applied quest
// operations that make retried requests idempotent.
public sealed record SavedQuests(
    int Version,
    SavedQuest Active,
    LevelQuestLog LevelLog,
    ElementaryQuestLog ElementaryLog,
    QuestCode[] Finished,
    int FinishedCount,
    bool SkillResetOffered,
    long Revision,
    SavedQuestOperation[] Operations,
    EliteQuestLog EliteLog = EliteQuestLog.None);

public static class QuestStateCodec
{
    public const int CurrentVersion = 1;

    public static SavedQuests Capture(QuestJournal journal)
    {
        var active = journal.Active;
        var saved = new SavedQuest(active.Code, active.State, active.Target, active.Kills, active.KillsNeeded, active.Variant, active.Timer, active.TimerMs,
            active.StartLevel, active.WeaponId, active.RingId, active.WeaponCreatedUnixMs, active.WeaponStage, active.WeaponKillsLeft, active.WeaponLifeBonus,
            active.WeaponManaRegenTenths, active.ArenaWon, active.Flags);
        var operations = StoredHistory.Newest(journal.Operations)
            .Select(static applied => new SavedQuestOperation(applied.Command.OperationId, applied.Command.NpcEntityId, applied.Command.Action,
                applied.Command.Code, applied.Command.State, applied.Command.Choice, applied.Revision))
            .ToArray();
        return new SavedQuests(CurrentVersion, saved, journal.LevelLog, journal.ElementaryLog, journal.Finished.ToArray(), journal.FinishedCount,
            journal.SkillResetOffered, journal.Revision, operations, journal.EliteLog);
    }

    public static QuestJournal Read(JsonText json)
    {
        var journal = new QuestJournal();
        if (CharacterStateCodec.IsBlank(json, JsonValueKind.Object))
        {
            return journal;
        }

        var state = JsonSerializer.Deserialize<SavedQuests>(json.Value, CharacterStateCodec.Options);
        if (state is null || state.Version != CurrentVersion || state.Active is null || state.Finished is null || state.Operations is null
            || state.Finished.Length > QuestJournal.FinishedCapacity || state.Operations.Length > OperationHistory<AppliedQuestOperation>.Capacity)
        {
            throw new InvalidDataException("Unsupported quest-state version or size.");
        }

        var saved = state.Active;
        if (saved.Code != QuestCode.None && !QuestCodes.IsDefined(saved.Code) || !Enum.IsDefined(saved.Timer) || saved.State < 0 || saved.Kills < 0
            || saved.KillsNeeded < 0 || saved.TimerMs < 0 || saved.WeaponStage < 0)
        {
            throw new InvalidDataException("Invalid saved quest.");
        }

        var active = new ActiveQuest
        {
            Code = saved.Code,
            State = saved.State,
            Target = saved.Target,
            Kills = saved.Kills,
            KillsNeeded = saved.KillsNeeded,
            Variant = saved.Variant,
            Timer = saved.Timer,
            TimerMs = saved.TimerMs,
            StartLevel = saved.StartLevel,
            WeaponId = saved.WeaponId,
            RingId = saved.RingId,
            WeaponCreatedUnixMs = saved.WeaponCreatedUnixMs,
            WeaponStage = saved.WeaponStage,
            WeaponKillsLeft = saved.WeaponKillsLeft,
            WeaponLifeBonus = saved.WeaponLifeBonus,
            WeaponManaRegenTenths = saved.WeaponManaRegenTenths,
            ArenaWon = saved.ArenaWon,
            Flags = saved.Flags
        };
        var operations = state.Operations.Select(static operation => new AppliedQuestOperation(
            new QuestCommand(operation.OperationId, operation.NpcEntityId, operation.Action, operation.Code, operation.State, operation.Choice),
            operation.Revision));
        try
        {
            journal.Restore(active, state.LevelLog, state.ElementaryLog, state.EliteLog, state.Finished, state.FinishedCount, state.SkillResetOffered,
                state.Revision, operations);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Invalid saved quest history.", exception);
        }

        return journal;
    }
}
