using System.Data.Common;
using PristonTale.Application.Chat;
using PristonTale.Application.Clans;
using PristonTale.Contracts.Social;
using PristonTale.Contracts.Texts;
using PristonTale.Persistence.Abstractions.Castle;
using PristonTale.Simulation.Social.Castle;

namespace PristonTale.Application.Castle;

// The server settings of Bless Castle: the mode and the weekly siege's day (0 Sunday .. 6 Saturday) and hour, local time.
public sealed record CastleOptions(CastleMode Mode, int SiegeDay, int SiegeHour)
{
    public static CastleOptions Default { get; } = new(CastleMode.Weekly, 0, 20);
}

// What a game master or a client reads of the castle.
public sealed record CastleView(long OwnerClanId, string OwnerName, int Tax, int NextTax, CastleClanSkill SkillInForce, int FountainPotions,
    SiegeState Siege, long SiegeStartedAt, long SiegeEndsAt, bool League);

public enum CastleOwnerResult : byte
{
    Set,
    NoClan,
    DuringSiege,
    StorageFailed
}

// Bless Castle on the server: the owner clan, the tax, the owner's setup, the soul fountain and the treasury (stored before
// every change applies, CastleRecord), and the siege's clock (kept in memory: a restart during a siege ends it). TickAsync
// runs it from the local time, minute by minute (a late tick catches up the minutes it missed, at most CatchUpMinutes):
// the tax and the clan skill chosen take effect each day at CastleRules.TaxHour (retried each minute until stored); a
// weekly siege is announced an hour and half an hour before its day and hour and starts then; in the league a siege
// restarts CastleRules.LeagueRestartMinutes after each end until the league's CastleRules.LeagueMinutes are over. A siege
// runs until its end time (the owner keeps the castle) or until the Tower of Valhalla falls, then stays over for
// CastleRules.OverSeconds before peace. The fountain gains a potion a minute in peace while the castle has an owner. An
// owner clan that no longer exists (checked at load and each minute, only while the clans are loaded) leaves the castle
// without one.
public sealed class CastleAppService
{
    private const int CatchUpMinutes = 60;

    // The siege's clock, replaced whole so that readers outside the gate see one consistent state.
    private sealed record SiegeClock(SiegeState State, DateTimeOffset StartedAt, DateTimeOffset EndsAt, DateTimeOffset OverUntil,
        DateTimeOffset LeagueEndsAt, DateTimeOffset LeagueRestartAt)
    {
        public static SiegeClock Peace { get; } = new(SiegeState.Peace, default, default, default, default, default);
    }

    private readonly ICastleRepository _repository;
    private readonly IClanDirectory _clans;
    private readonly ChatDirectory _chat;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CastleRecord _record = CastleRecord.Empty;
    private SiegeClock _siege = SiegeClock.Peace;
    private DateTimeOffset _nextPotionAt;
    private long _lastMinute = -1;
    private bool _taxDue;
    private bool _storageFailed;

    public CastleAppService(ICastleRepository repository, IClanDirectory clans, ChatDirectory chat, TimeProvider? clock = null)
    {
        _repository = repository;
        _clans = clans;
        _chat = chat;
        _clock = clock ?? TimeProvider.System;
    }

    public bool Available { get; private set; }

    public IClanDirectory Clans => _clans;

    public long OwnerClanId => Volatile.Read(ref _record).OwnerClanId;

    public SiegeState Siege => Volatile.Read(ref _siege).State;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var record = await _repository.LoadAsync(ct);
        await _gate.WaitAsync(ct);
        try
        {
            if (OwnerGone(record))
            {
                record = record with { OwnerClanId = 0 };
                await _repository.SaveAsync(record, ct);
            }

            Volatile.Write(ref _record, record);
            Volatile.Write(ref _siege, SiegeClock.Peace);
            _nextPotionAt = _clock.GetUtcNow().AddSeconds(CastleRules.FountainPotionSeconds);
            Available = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public CastleView View()
    {
        var record = Volatile.Read(ref _record);
        var siege = Volatile.Read(ref _siege);
        return new CastleView(record.OwnerClanId, _clans.NameOf(record.OwnerClanId) ?? "", record.Tax, record.NextTax,
            Enum.IsDefined((CastleClanSkill)record.SkillInForce) ? (CastleClanSkill)record.SkillInForce : CastleClanSkill.None,
            record.FountainPotions, siege.State, siege.StartedAt.ToUnixTimeSeconds(), siege.EndsAt.ToUnixTimeSeconds(),
            siege.LeagueEndsAt > _clock.GetUtcNow());
    }

    // The schedule, the siege's clock and the fountain, from the settings in force; called every second. False when a
    // storage it tried was refused (the change is tried again on a later minute).
    public async Task<bool> TickAsync(CastleOptions options)
    {
        if (!Available)
        {
            return true;
        }

        await _gate.WaitAsync();
        try
        {
            _storageFailed = false;
            var now = _clock.GetUtcNow();
            var minute = now.ToUnixTimeSeconds() / 60;
            var first = _lastMinute < 0 ? minute : Math.Max(_lastMinute + 1, minute - CatchUpMinutes + 1);
            for (var at = first; at <= minute; at++)
            {
                await OnMinuteAsync(TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(at * 60), _clock.LocalTimeZone), options);
            }

            _lastMinute = minute;
            StepSiege(now, options);
            await StepFountainAsync(now);
            return !_storageFailed;
        }
        finally
        {
            _gate.Release();
        }
    }

    // A game master starts a siege now, lasting the minutes given (CastleRules.SiegeMinutes when 0 or less; never past a
    // running league's end).
    public async Task<bool> StartSiegeAsync(int minutes = 0)
    {
        await _gate.WaitAsync();
        try
        {
            return BeginSiege(minutes > 0 ? minutes : CastleRules.SiegeMinutes);
        }
        finally
        {
            _gate.Release();
        }
    }

    // A game master ends the siege now, and the league with it; the owner does not change.
    public async Task<bool> EndSiegeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_siege.State != SiegeState.Running)
            {
                return false;
            }

            Publish(_siege with { LeagueEndsAt = default });
            CloseSiege();
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    // A game master sets the running siege to end this many minutes from now.
    public async Task<bool> SetSiegeMinutesAsync(int minutes)
    {
        await _gate.WaitAsync();
        try
        {
            if (_siege.State != SiegeState.Running)
            {
                return false;
            }

            Publish(_siege with { EndsAt = _clock.GetUtcNow().AddMinutes(minutes) });
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    // A game master's owner, outside a siege; 0 leaves the castle without an owner.
    public async Task<CastleOwnerResult> SetOwnerAsync(long clanId)
    {
        await _gate.WaitAsync();
        try
        {
            if (_siege.State != SiegeState.Peace)
            {
                return CastleOwnerResult.DuringSiege;
            }

            if (clanId != 0 && _clans.NameOf(clanId) is null)
            {
                return CastleOwnerResult.NoClan;
            }

            return await StoreAsync(Volatile.Read(ref _record) with { OwnerClanId = clanId }) ? CastleOwnerResult.Set : CastleOwnerResult.StorageFailed;
        }
        finally
        {
            _gate.Release();
        }
    }

    // The next tax and the chosen clan skill take effect now (as at CastleRules.TaxHour).
    public async Task<bool> ApplyTaxAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return await ApplyTaxLockedAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    // The fountain's next potion comes CastleRules.FountainPotionSeconds from now.
    public async Task ResetFountainTimeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _nextPotionAt = _clock.GetUtcNow().AddSeconds(CastleRules.FountainPotionSeconds);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task OnMinuteAsync(DateTimeOffset local, CastleOptions options)
    {
        var record = Volatile.Read(ref _record);
        if (OwnerGone(record))
        {
            await StoreAsync(record with { OwnerClanId = 0 });
        }

        _taxDue |= local.Hour == CastleRules.TaxHour && local.Minute == 0;
        if (_taxDue)
        {
            await ApplyTaxLockedAsync();
        }

        if (options.Mode == CastleMode.Off || _siege.State != SiegeState.Peace)
        {
            return;
        }

        var start = NextSiege(local, options);
        if (Same(local, start.AddHours(-1)))
        {
            Notice(GameTextId.SiegeInAnHour);
        }
        else if (Same(local, start.AddMinutes(-30)))
        {
            Notice(GameTextId.SiegeInHalfAnHour);
        }
        else if (Same(local, start))
        {
            if (options.Mode == CastleMode.League)
            {
                Publish(_siege with { LeagueEndsAt = _clock.GetUtcNow().AddMinutes(CastleRules.LeagueMinutes) });
                Notice(GameTextId.SiegeLeague);
            }

            BeginSiege(CastleRules.SiegeMinutes);
        }
    }

    // A league stops restarting once the mode leaves League.
    private void StepSiege(DateTimeOffset now, CastleOptions options)
    {
        var siege = _siege;
        if (options.Mode != CastleMode.League && siege.LeagueEndsAt != default)
        {
            siege = siege with { LeagueEndsAt = default, LeagueRestartAt = default };
            Publish(siege);
        }

        if (siege.State == SiegeState.Running && now >= siege.EndsAt)
        {
            CloseSiege();
        }
        else if (siege.State == SiegeState.Over && now >= siege.OverUntil)
        {
            var restart = now.AddMinutes(CastleRules.LeagueRestartMinutes);
            var restarts = restart < siege.LeagueEndsAt;
            Publish(siege with { State = SiegeState.Peace, LeagueRestartAt = restarts ? restart : default });
            Notice(GameTextId.SiegeOver);
            if (restarts)
            {
                Notice(GameTextId.SiegeRestartsSoon);
            }
        }
        else if (siege.State == SiegeState.Peace && siege.LeagueRestartAt != default && now >= siege.LeagueRestartAt)
        {
            BeginSiege(CastleRules.SiegeMinutes);
        }
    }

    private async Task StepFountainAsync(DateTimeOffset now)
    {
        var record = Volatile.Read(ref _record);
        if (_siege.State != SiegeState.Peace || record.OwnerClanId == 0 || now < _nextPotionAt)
        {
            return;
        }

        _nextPotionAt = now.AddSeconds(CastleRules.FountainPotionSeconds);
        if (record.FountainPotions < CastleRules.FountainMaxPotions)
        {
            await StoreAsync(record with { FountainPotions = record.FountainPotions + 1 });
        }
    }

    private bool BeginSiege(int minutes)
    {
        var siege = _siege;
        if (siege.State != SiegeState.Peace)
        {
            return false;
        }

        var now = _clock.GetUtcNow();
        var ends = now.AddMinutes(minutes);
        Publish(siege with
        {
            State = SiegeState.Running,
            StartedAt = now,
            EndsAt = siege.LeagueEndsAt > now && siege.LeagueEndsAt < ends ? siege.LeagueEndsAt : ends,
            LeagueRestartAt = default
        });
        Notice(GameTextId.SiegeStarted);
        return true;
    }

    private void CloseSiege() => Publish(_siege with { State = SiegeState.Over, OverUntil = _clock.GetUtcNow().AddSeconds(CastleRules.OverSeconds) });

    private void Publish(SiegeClock siege) => Volatile.Write(ref _siege, siege);

    private bool OwnerGone(CastleRecord record) => record.OwnerClanId != 0 && _clans.Available && _clans.NameOf(record.OwnerClanId) is null;

    private async Task<bool> ApplyTaxLockedAsync()
    {
        var record = Volatile.Read(ref _record);
        var applied = await StoreAsync(record with { Tax = record.NextTax, SkillInForce = record.ClanSkill });
        _taxDue &= !applied;
        return applied;
    }

    // Stored first; a refused storage changes nothing.
    private async Task<bool> StoreAsync(CastleRecord record)
    {
        try
        {
            await _repository.SaveAsync(record);
        }
        catch (DbException)
        {
            _storageFailed = true;
            return false;
        }

        Volatile.Write(ref _record, record);
        return true;
    }

    private void Notice(GameTextId text) => _chat.Broadcast(new ChatLine(ChatKind.Notice, 0, "", GameText.Of(text)));

    // The coming siege: its day and hour this week, or next week's once that minute has passed. A siege hour that a
    // daylight-saving change skips does not come that week.
    public static DateTimeOffset NextSiege(DateTimeOffset local, CastleOptions options)
    {
        var today = new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, local.Offset);
        var start = today.AddDays((options.SiegeDay - (int)local.DayOfWeek + 7) % 7).AddHours(options.SiegeHour);
        return start.AddMinutes(1) <= local ? start.AddDays(7) : start;
    }

    private static bool Same(DateTimeOffset a, DateTimeOffset b) =>
        a.Year == b.Year && a.Month == b.Month && a.Day == b.Day && a.Hour == b.Hour && a.Minute == b.Minute;
}
