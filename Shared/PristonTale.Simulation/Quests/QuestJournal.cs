using PristonTale.Simulation.Characters;

namespace PristonTale.Simulation.Quests;

// The running quest. Only one quest runs at a time: every quest refuses to start while another one is active.
// Target is the quest code of the monster being hunted (0 when nothing is hunted); Kills counts it up to KillsNeeded.
// A timed hunt keeps TimerMs of play time; when it runs out the hunt step is lost. Weapon* describe the growing quest
// weapon of the second rank-up while it lasts.
public sealed record ActiveQuest
{
    public static readonly ActiveQuest Idle = new();

    public QuestCode Code { get; init; }
    public int State { get; init; }
    public int Target { get; init; }
    public int Kills { get; init; }
    public int KillsNeeded { get; init; }

    // Variant of the quest: the prey family of the level 90 bonus chain (1..3) or the chosen Fury reward (1..8).
    public int Variant { get; init; }

    public QuestTimerKind Timer { get; init; }
    public long TimerMs { get; init; }

    public int StartLevel { get; init; }
    public Guid WeaponId { get; init; }
    public Guid RingId { get; init; }
    public long WeaponCreatedUnixMs { get; init; }
    public int WeaponStage { get; init; }
    public int WeaponKillsLeft { get; init; }
    public int WeaponLifeBonus { get; init; }
    public int WeaponManaRegenTenths { get; init; }

    // The arena opponent of the current duel was beaten; the next talk moves the quest on.
    public bool ArenaWon { get; init; }

    // An elite quest's crystals, item use and kill, and the items its kills earned that wait for the upkeep (EliteQuests).
    public int Flags { get; init; }

    public bool IsIdle => Code == QuestCode.None;

    public bool HuntComplete => KillsNeeded > 0 && Kills >= KillsNeeded;

    public ActiveQuest Hunting(int target, int killsNeeded) => this with { Target = target, Kills = 0, KillsNeeded = killsNeeded };

    public ActiveQuest StopHunt() => this with { Target = 0, Kills = 0, KillsNeeded = 0, Timer = QuestTimerKind.None, TimerMs = 0 };
}

public enum QuestTimerKind : byte
{
    None = 0,

    // The quest timer icon shows the hunt but never runs out (beginner quests and Fury of Phantom).
    Display = 1,

    // A hunt against the clock: TimerMs of play time counts down in every field.
    Expiring = 2
}

public sealed record AppliedQuestOperation(QuestCommand Command, long Revision) : IAppliedOperation
{
    Guid IAppliedOperation.OperationId => Command?.OperationId ?? Guid.Empty;
}

public sealed class QuestJournal
{
    public const int FinishedCapacity = 32;

    private readonly List<QuestCode> _finished = [];
    private readonly OperationHistory<AppliedQuestOperation> _operations = new();
    private ActiveQuest _active = ActiveQuest.Idle;
    private LevelQuestLog _levelLog;
    private ElementaryQuestLog _elementaryLog;
    private EliteQuestLog _eliteLog;
    private bool _skillResetOffered;
    private bool _weaponSlowed;

    // Every change bumps the revision; the published quest view follows it.
    public long Revision { get; private set; }

    public ActiveQuest Active
    {
        get => _active;
        set
        {
            _active = value ?? ActiveQuest.Idle;
            Revision++;
        }
    }

    public LevelQuestLog LevelLog
    {
        get => _levelLog;
        set
        {
            _levelLog = value;
            Revision++;
        }
    }

    public ElementaryQuestLog ElementaryLog
    {
        get => _elementaryLog;
        set
        {
            _elementaryLog = value;
            Revision++;
        }
    }

    public EliteQuestLog EliteLog
    {
        get => _eliteLog;
        set
        {
            _eliteLog = value;
            Revision++;
        }
    }

    // A rank-up offers one free redistribution of the learned skills until it is used.
    public bool SkillResetOffered
    {
        get => _skillResetOffered;
        set
        {
            _skillResetOffered = value;
            Revision++;
        }
    }

    // Session only: the quest weapon loses two points of attack speed once its holder has gained more than two levels
    // since receiving it without finishing the hunt.
    public bool WeaponSlowed
    {
        get => _weaponSlowed;
        set
        {
            _weaponSlowed = value;
            Revision++;
        }
    }

    // The finished-quest history keeps the last 32 codes, oldest first; FinishedCount counts every finished quest.
    public IReadOnlyList<QuestCode> Finished => _finished;
    public int FinishedCount { get; private set; }

    public OperationHistory<AppliedQuestOperation> Operations => _operations;

    public bool HasFinished(QuestCode code) => _finished.Contains(code);

    public void Finish(QuestCode code)
    {
        if (_finished.Count == FinishedCapacity)
        {
            _finished.RemoveAt(0);
        }

        _finished.Add(code);
        FinishedCount++;
        Revision++;
    }

    public void Record(QuestCommand command)
    {
        _operations.Record(new AppliedQuestOperation(command, Revision + 1));
        Revision++;
    }

    public AppliedQuestOperation Find(Guid operationId) => _operations.Find(operationId);

    public void Restore(ActiveQuest active, LevelQuestLog levelLog, ElementaryQuestLog elementaryLog, EliteQuestLog eliteLog, IEnumerable<QuestCode> finished,
        int finishedCount, bool skillResetOffered, long revision, IEnumerable<AppliedQuestOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(active);
        if (revision < 0 || finishedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        _finished.Clear();
        foreach (var code in finished)
        {
            if (_finished.Count == FinishedCapacity || !QuestCodes.IsDefined(code))
            {
                throw new ArgumentException("Invalid finished-quest history.");
            }

            _finished.Add(code);
        }

        _operations.Restore(operations, static _ => true, "quest operation");

        _active = active;
        _levelLog = levelLog;
        _elementaryLog = elementaryLog;
        _eliteLog = eliteLog;
        FinishedCount = Math.Max(finishedCount, _finished.Count);
        _skillResetOffered = skillResetOffered;
        _weaponSlowed = false;
        Revision = revision;
    }

    public QuestJournal Clone()
    {
        var clone = new QuestJournal();
        clone.Restore(_active, _levelLog, _elementaryLog, _eliteLog, _finished, FinishedCount, _skillResetOffered, Revision, _operations);
        clone._weaponSlowed = _weaponSlowed;
        return clone;
    }

    // Kills of the same hunt step counted on the world tick while a durable quest operation was being stored are carried
    // into the committed journal; any other tick-side change during that window is superseded by the committed one.
    public void AdoptCommitted(QuestJournal committed)
    {
        ArgumentNullException.ThrowIfNull(committed);
        var live = _active;
        var adopted = committed._active;
        if (adopted.Code == live.Code && adopted.State == live.State && adopted.Target == live.Target && adopted.Variant == live.Variant)
        {
            adopted = adopted with
            {
                Kills = Math.Max(adopted.Kills, live.Kills),
                TimerMs = adopted.Timer == QuestTimerKind.Expiring && live.Timer == QuestTimerKind.Expiring ? Math.Min(adopted.TimerMs, live.TimerMs) : adopted.TimerMs,
                WeaponKillsLeft = adopted.WeaponStage == live.WeaponStage ? Math.Min(adopted.WeaponKillsLeft, live.WeaponKillsLeft) : adopted.WeaponKillsLeft
            };
        }

        var revision = Math.Max(Revision, committed.Revision) + 1;
        Restore(adopted, committed._levelLog, committed._elementaryLog, committed._eliteLog, committed._finished, committed.FinishedCount,
            committed._skillResetOffered, revision, committed._operations);
        _weaponSlowed = committed._weaponSlowed;
    }
}
