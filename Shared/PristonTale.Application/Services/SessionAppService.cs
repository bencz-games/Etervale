using PristonTale.Simulation.Progression;
using PristonTale.Simulation.World.Npcs;
using System.Numerics;
using System.Text.Json;
using PristonTale.Application.Saves;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World;
using PristonTale.Contracts.Sessions;
using PristonTale.Contracts.Characters;

namespace PristonTale.Application.Services;

public sealed class SessionAppService
{
    private readonly ISessionRegistry _sessions;
    private readonly CharacterClassRegistry _classes;
    private readonly ICharacterRepository _characters;
    private readonly ICharacterSaves _saves;
    private readonly WorldCatalog _world;
    private readonly WorldServer? _host;
    private readonly DurableOperations _durable;
    private readonly WorldDataHolder _worldData;
    private readonly TimeProvider _clock;

    public SessionAppService(
        ISessionRegistry sessions,
        CharacterClassRegistry classes,
        ICharacterRepository characters,
        ICharacterSaves saves,
        WorldCatalog world,
        WorldServer? host = null,
        WorldDataHolder? worldData = null,
        TimeProvider? clock = null)
    {
        _sessions = sessions;
        _classes = classes;
        _characters = characters;
        _saves = saves;
        _durable = new DurableOperations(saves);
        _world = world;
        _host = host;
        _worldData = worldData ?? new WorldDataHolder();
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<EnterWorldOutcome> EnterWorldAsync(string sessionToken, string characterName, CancellationToken ct = default)
    {
        if (!_sessions.TryGet(sessionToken, out var session))
        {
            return EnterWorldOutcome.Denied(EnterWorldFailure.InvalidSession);
        }

        if (!session.TryBeginEntry())
        {
            return EnterWorldOutcome.Denied(EnterWorldFailure.AlreadyEntering);
        }

        long? claimedCharacter = null;
        var ready = false;
        try
        {
            var found = await _characters.FindByNameForAccountAsync(session.AccountId, characterName ?? string.Empty, ct);
            if (found is null)
            {
                return EnterWorldOutcome.Denied(EnterWorldFailure.NotFound);
            }

            if (!_sessions.TryClaimCharacter(found.Id, sessionToken))
            {
                return EnterWorldOutcome.Denied(EnterWorldFailure.AlreadyActive);
            }

            claimedCharacter = found.Id;
            // The previous session's leave save may have been stored, and its claim released, after the first read: the
            // record is read again under the claim, when nothing else writes it.
            var record = await _characters.FindByNameForAccountAsync(session.AccountId, found.Name, ct);
            if (record is null || record.Id != found.Id)
            {
                return EnterWorldOutcome.Denied(EnterWorldFailure.NotFound);
            }

            if (!_world.TryGetFieldByCode(record.FieldId, out var field) || !Enum.IsDefined(typeof(PlayerClass), record.JobCode))
            {
                return EnterWorldOutcome.Denied(EnterWorldFailure.InvalidSavedClassOrField);
            }
            var attributes = new CharacterAttributes(record.Strength, record.Spirit, record.Talent, record.Agility, record.Health);
            var character = new PlayerCharacter(_classes.Get((PlayerClass)record.JobCode), record.Name, record.Level, attributes);
            try
            {
                CharacterStateCodec.Restore(character, record, _worldData.Current.Items);
                SavedPremium.Restore(character, record.Premium, _clock.GetUtcNow().ToUnixTimeSeconds());
            }
            catch (Exception exception) when (exception is InvalidDataException or JsonException or ArgumentException)
            {
                return EnterWorldOutcome.Denied(EnterWorldFailure.UnsupportedSavedState);
            }
            if (record.Life > 0)
            {
                character.Life = Math.Clamp(record.Life, 1, character.MaxLife);
                character.Mana = Math.Clamp(record.Mana, 0, character.MaxMana);
                character.Stamina = Math.Clamp(record.Stamina, 0, character.MaxStamina);
            }

            var spawn = ResolveSpawn(record, field);
            if (WorldServer.StartsAway(field.FieldCode) && _world.TryGetFieldByCode(QuestClasses.HomeField(character), out var home))
            {
                field = home;
                spawn = WorldServer.HomeStart(_world, home.FieldCode);
            }

            if (!_sessions.TryGet(sessionToken, out var current) || !ReferenceEquals(current, session))
            {
                return EnterWorldOutcome.Denied(EnterWorldFailure.SessionClosed);
            }

            session.CharacterName = record.Name;
            session.Character = character;
            session.Loaded = record;
            session.ResumeSaves(record.SaveSeq);
            session.FieldId = field.FieldCode;
            session.SpawnPosition = spawn;
            session.InVillage = field.State == FieldState.Village;
            if (!session.CompleteEntry(true))
            {
                return EnterWorldOutcome.Denied(EnterWorldFailure.SessionClosed);
            }

            ready = true;
            return EnterWorldOutcome.Success(session.FieldId, session.EntityId, spawn.X, spawn.Z, $"entered field {session.FieldId}");
        }
        finally
        {
            if (!ready && claimedCharacter.HasValue)
            {
                _sessions.ReleaseCharacter(claimedCharacter.Value, sessionToken);
            }

            if (!ready)
            {
                session.CompleteEntry(false);
            }
        }
    }

    private static Vector3 ResolveSpawn(CharacterRecord record, FieldDef field)
    {
        var saved = new Vector3(record.PosX, 0f, record.PosZ);
        if (WorldPosition.IsValid(saved) && (record.PosX != 0f || record.PosZ != 0f))
        {
            return saved;
        }

        return new Vector3(field.Arrival.X, 0f, field.Arrival.Z);
    }

    public Task<bool> SaveCharacterAsync(string sessionToken, CancellationToken ct = default) => SaveAsync(sessionToken, null, ct);

    // Stores the capture the world took as the player left (WorldServer.LeaveAsync). A leave save completes once storage
    // has it (or, as the server stops, the save journal), so the character is not released before.
    public Task<bool> SaveLeftCharacterAsync(string sessionToken, CharacterSaveSnapshot left, CancellationToken ct = default) =>
        SaveAsync(sessionToken, left, ct);

    private async Task<bool> SaveAsync(string sessionToken, CharacterSaveSnapshot? left, CancellationToken ct)
    {
        if (!_sessions.TryGet(sessionToken, out var session))
        {
            return false;
        }

        await session.SaveGate.WaitAsync(ct);
        try
        {
            if (session.Character is null || session.Loaded is null)
            {
                return false;
            }

            var snapshot = left ?? (_host is null || !session.IsPlaying && !session.LeftDead
                ? CharacterRecordFactory.Capture(session.Loaded, session.Character, session.FieldId, session.SpawnPosition)
                : session.IsPlaying ? await _host.CaptureForSaveAsync(session) : await _host.CaptureLeftAsync(session));
            if (snapshot is null)
            {
                return false;
            }

            return await _saves.SaveAsync(session, snapshot);
        }
        finally
        {
            session.SaveGate.Release();
        }
    }

    public Task<InventoryOutcome> ApplyInventoryAsync(string sessionToken, InventoryCommand command, CancellationToken ct = default)
    {
        return ApplyDurableAsync(sessionToken, session => _host!.PrepareInventoryAsync(session, command),
            (session, operation) => _host!.CommitInventoryAsync(session, operation),
            message => new InventoryOutcome(false, 0, message), ct);
    }

    public Task<ProgressionOutcome> ApplyProgressionAsync(
        string sessionToken, ProgressionCommand command, CancellationToken ct = default)
    {
        return ApplyDurableAsync(sessionToken, session => _host!.PrepareProgressionAsync(session, command),
            (session, operation) => _host!.CommitProgressionAsync(session, operation),
            message => new ProgressionOutcome(false, 0, message), ct);
    }

    private async Task<TOutcome> ApplyDurableAsync<TOutcome>(string token,
        Func<PlayerSession, Task<PreparedCharacterOperation<TOutcome>?>> prepare,
        Func<PlayerSession, PreparedCharacterOperation<TOutcome>, Task<bool>> commit,
        Func<string, TOutcome> rejected, CancellationToken ct)
    {
        if (_host is null)
        {
            return rejected(DurableFailures.Message(DurableFailure.NoSession));
        }

        using var lease = await _durable.AdmitAsync(_sessions, token, ct);
        return await lease.RunAsync(prepare, commit, prepared => prepared.Outcome,
            failure => rejected(DurableFailures.Message(failure)),
            // An item or gold thrown to the ground is stored before it lands there, so no crash keeps it in the bag and on
            // the ground; any other change is stored in the background.
            async (session, prepared) => await _characters.UpdateAsync(session.Stamp(prepared.Snapshot!), CancellationToken.None)
                ? null
                : DurableFailure.StorageFailed,
            storeFirst: static prepared => prepared is PreparedThrowOperation);
    }
}
