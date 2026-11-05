using System.Numerics;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.Sessions;

public sealed class PlayerSession
{
    // 0 = authenticated, 1 = loading, 2 = ready, 3 = playing, 4 = closing.
    private int _state;

    // The save sequence of the character's last capture (CharacterRecord.SaveSeq).
    private long _saveSeq;

    internal SemaphoreSlim SaveGate { get; } = new(1, 1);
    internal bool TryBeginEntry() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;
    internal bool CompleteEntry(bool success) => Interlocked.CompareExchange(ref _state, success ? 2 : 0, 1) == 1;
    internal void MarkClosed() => Interlocked.Exchange(ref _state, 4);
    public bool IsPlaying => Volatile.Read(ref _state) == 3;
    public bool TryBeginPlay() => Interlocked.CompareExchange(ref _state, 3, 2) == 2;
    public void EndPlay() => Interlocked.CompareExchange(ref _state, 4, 3);

    public required string SessionToken { get; init; }
    public required long AccountId { get; init; }
    public required string UserId { get; init; }
    public required ulong EntityId { get; init; }
    public string Language { get; set; } = "en";

    // The account's game-master level at login (0 for players).
    public int GmLevel { get; set; }

    public string? CharacterName { get; set; }
    public int FieldId { get; set; }
    public Vector3 SpawnPosition { get; set; }

    public PlayerCharacter? Character { get; set; }
    public bool InVillage { get; set; }

    // A used mobile shop item lets the player sell and repair without a shop NPC until the shop is closed.
    public bool MobileShopOpen { get; set; }

    // The character left the world dead while a durable operation held its save gate: the save after it stores the town
    // restart with its costs (WorldServer.CaptureLeftAsync).
    public bool LeftDead { get; set; }

    public CharacterRecord? Loaded { get; set; }

    // Captures of the loaded character continue the sequence storage holds.
    internal void ResumeSaves(long storedSeq) => Interlocked.Exchange(ref _saveSeq, storedSeq);

    public long NextSaveSeq() => Interlocked.Increment(ref _saveSeq);

    // A capture stored at once, ahead of every earlier capture of this session (a transfer between characters).
    public CharacterRecord Stamp(CharacterSaveSnapshot snapshot) => snapshot.Encode() with { SaveSeq = NextSaveSeq() };

    // The account warehouse as last loaded or committed, while this session has it open; changed only under SaveGate.
    internal WarehouseStorage? Warehouse { get; set; }
}
