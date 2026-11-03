namespace PristonTale.Simulation.Items.Usable;

// The timed effects a premium item keeps running. The numbers are persisted and sent to the client; never renumber.
public enum PremiumEffectKind : byte
{
    ThirdEyes = 1,
    ExperienceUp = 2,
    VampiricCuspid = 3,
    ManaRecharging = 4,
    MightOfAwell = 5,
    ManaReduce = 6,
    PhoenixPet = 7,
    HelpPetTerry = 8,
    HelpPetNepsys = 9,
    HelpPetIo = 10,
    HelpPetMute = 11,
    VampiricCuspidEx = 12,
    StaminaReduce = 13,
    LifeBooster = 14,
    ManaBooster = 15,
    StaminaBooster = 16,
    SkillDelay = 17
}

// How a new timer conflicts with the running ones: a single item refuses its own kind, a package refuses either of its
// two kinds, a helper pet refuses while any helper pet runs.
public enum PremiumConflict : byte
{
    SameKind = 0,
    ThirdEyesPackage = 1,
    ManaReducePackage = 2,
    HelperPet = 3
}

public readonly record struct PremiumTimer(PremiumEffectKind Kind, int Value, long DurationSeconds, long ExpiresAtSeconds)
{
    public long RemainingSeconds(long nowSeconds) => Math.Max(0, ExpiresAtSeconds - nowSeconds);
}

public enum PremiumStartResult
{
    Started,
    AlreadyRunning,
    SameAttribute,
    Full
}

// The running premium timers of one character, its Gravity Scroll count and the big-head look. Timers run on the real clock (Unix seconds), also while the character
// is offline, and end when their expiry passes.
public sealed class PremiumTimers
{
    public const int MaxTimers = 17;
    public const int SoccerHeadMode = Characters.CharacterHead.SoccerLook;
    private const int MaxHeadMode = Characters.CharacterHead.SoccerLook;

    private readonly List<PremiumTimer> _timers = new();

    public long Revision { get; private set; }

    // The real clock (Unix seconds) of the last expiry pass; a save records it so offline time can be measured.
    public long ClockSeconds { get; private set; }

    // Gravity Scrolls used, each adding carrying capacity for good.
    public int GravityScrolls { get; private set; }

    // The head look of the big-head potion (mode 1 or 2) or the soccer-ball potion (mode 3) and its real-clock end.
    public int BigHeadMode { get; private set; }
    public long BigHeadExpiresAtSeconds { get; private set; }

    public IReadOnlyList<PremiumTimer> Timers => _timers;

    public event Action Changed;

    public static bool IsHelperPet(PremiumEffectKind kind) => kind is PremiumEffectKind.HelpPetTerry or PremiumEffectKind.HelpPetNepsys
        or PremiumEffectKind.HelpPetIo or PremiumEffectKind.HelpPetMute;

    // Boosters and the skill-delay item keep their remaining time while the character is offline and show in the
    // running-effects bar instead of the premium column.
    public static bool PausesOffline(PremiumEffectKind kind) => kind is PremiumEffectKind.LifeBooster or PremiumEffectKind.ManaBooster
        or PremiumEffectKind.StaminaBooster or PremiumEffectKind.SkillDelay;

    public static bool IsVampiric(PremiumEffectKind kind) => kind is PremiumEffectKind.VampiricCuspid or PremiumEffectKind.VampiricCuspidEx;

    public bool IsRunning(PremiumEffectKind kind, long nowSeconds) => Find(kind) is { } timer && timer.ExpiresAtSeconds > nowSeconds;

    // The timer of a kind as last expired by the tick (Expire runs every tick), or null.
    public PremiumTimer? Find(PremiumEffectKind kind)
    {
        foreach (var timer in _timers)
        {
            if (timer.Kind == kind)
            {
                return timer;
            }
        }

        return null;
    }

    public bool IsActive(PremiumEffectKind kind) => Find(kind) is not null;

    // The value of a running timer (a percent, or carrying capacity for Might of Awell), or 0.
    public int ValueOf(PremiumEffectKind kind) => Find(kind)?.Value ?? 0;

    public PremiumStartResult CanStart(PremiumEffectKind kind, PremiumConflict conflict, long nowSeconds)
    {
        if (IsVampiric(kind) && (IsRunning(PremiumEffectKind.VampiricCuspid, nowSeconds) || IsRunning(PremiumEffectKind.VampiricCuspidEx, nowSeconds)))
        {
            return PremiumStartResult.SameAttribute;
        }

        var blocked = conflict switch
        {
            PremiumConflict.ThirdEyesPackage => IsRunning(PremiumEffectKind.ThirdEyes, nowSeconds) || IsRunning(PremiumEffectKind.ExperienceUp, nowSeconds),
            PremiumConflict.ManaReducePackage => IsRunning(PremiumEffectKind.ManaReduce, nowSeconds) || IsRunning(PremiumEffectKind.ExperienceUp, nowSeconds),
            PremiumConflict.HelperPet => _timers.Any(timer => IsHelperPet(timer.Kind) && timer.ExpiresAtSeconds > nowSeconds),
            _ => IsRunning(kind, nowSeconds)
        };
        if (blocked)
        {
            return conflict == PremiumConflict.HelperPet ? PremiumStartResult.SameAttribute : PremiumStartResult.AlreadyRunning;
        }

        return _timers.Count(timer => timer.ExpiresAtSeconds > nowSeconds) >= MaxTimers ? PremiumStartResult.Full : PremiumStartResult.Started;
    }

    public void Start(PremiumEffectKind kind, int value, long durationSeconds, long nowSeconds)
    {
        if (!Enum.IsDefined(kind) || durationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Expire(nowSeconds);
        _timers.RemoveAll(timer => timer.Kind == kind);
        _timers.Add(new PremiumTimer(kind, value, durationSeconds, nowSeconds + durationSeconds));
        Touch();
    }

    // Drops the timers whose expiry passed; true when one ended.
    public bool Expire(long nowSeconds)
    {
        ClockSeconds = Math.Max(ClockSeconds, nowSeconds);
        // Checked every tick for every player: no closure unless a timer actually ended.
        var ended = false;
        foreach (var timer in _timers)
        {
            if (timer.ExpiresAtSeconds <= nowSeconds)
            {
                ended = true;
                break;
            }
        }

        if (ended)
        {
            _timers.RemoveAll(timer => timer.ExpiresAtSeconds <= nowSeconds);
        }

        if (BigHeadMode != 0 && BigHeadExpiresAtSeconds <= nowSeconds)
        {
            BigHeadMode = 0;
            BigHeadExpiresAtSeconds = 0;
            ended = true;
        }

        if (ended)
        {
            Touch();
        }

        return ended;
    }

    public bool TryAddGravityScroll(int maximum)
    {
        if (GravityScrolls >= maximum)
        {
            return false;
        }

        GravityScrolls++;
        Touch();
        return true;
    }

    public bool TryStartBigHead(int mode, long durationSeconds, long nowSeconds)
    {
        if (BigHeadMode != 0 && BigHeadExpiresAtSeconds > nowSeconds || mode is < 1 or > MaxHeadMode)
        {
            return false;
        }

        BigHeadMode = mode;
        BigHeadExpiresAtSeconds = nowSeconds + durationSeconds;
        Touch();
        return true;
    }

    public void RestoreCounters(int gravityScrolls, int bigHeadMode, long bigHeadExpiresAtSeconds)
    {
        if (gravityScrolls < 0 || bigHeadMode is < 0 or > MaxHeadMode)
        {
            throw new ArgumentException("Invalid premium counters.");
        }

        GravityScrolls = gravityScrolls;
        BigHeadMode = bigHeadMode;
        BigHeadExpiresAtSeconds = bigHeadMode == 0 ? 0 : bigHeadExpiresAtSeconds;
        Touch();
    }

    public void Restore(IEnumerable<PremiumTimer> timers)
    {
        _timers.Clear();
        foreach (var timer in timers)
        {
            if (!Enum.IsDefined(timer.Kind) || timer.DurationSeconds <= 0 || _timers.Count >= MaxTimers || _timers.Any(existing => existing.Kind == timer.Kind))
            {
                throw new ArgumentException("Invalid premium timer.");
            }

            _timers.Add(timer);
        }

        Touch();
    }

    public void CopyFrom(PremiumTimers other)
    {
        _timers.Clear();
        _timers.AddRange(other._timers);
        ClockSeconds = other.ClockSeconds;
        GravityScrolls = other.GravityScrolls;
        BigHeadMode = other.BigHeadMode;
        BigHeadExpiresAtSeconds = other.BigHeadExpiresAtSeconds;
        Touch();
    }

    private void Touch()
    {
        Revision++;
        Changed?.Invoke();
    }
}
