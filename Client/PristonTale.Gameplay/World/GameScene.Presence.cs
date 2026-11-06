using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Client.Net;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Physics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Combat;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Gameplay.Audio;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Gameplay.World.Streaming;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Physics;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Motion;
using NumVector3 = System.Numerics.Vector3;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Combat;
using PristonTale.Engine3D;
using PristonTale.Assets;
using PristonTale.Contracts.Characters;

namespace PristonTale.Gameplay.World;

// The presence stream: the play channel, the reader task, draining updates on the game thread and handing them to the remote playback, effects and observers.
public sealed partial class GameScene
{
    public void SetWorldRenderCatalog(PristonTale.Client.Net.WorldCatalog.WorldRenderCatalog catalog)
    {
        if (catalog == null)
            return;
        _worldRenderCatalog = catalog;
        _pendingCatalog = catalog;
    }

    public void BeginRemotePresence(IPlayChannel channel, EnteredCharacter entered)
    {
        if (channel == null)
        {
            throw new ArgumentNullException(nameof(channel));
        }

        if (_renderer == null)
        {
            throw new InvalidOperationException("BeginRemotePresence requires a loaded field (call LoadEntered first).");
        }

        if (_remote != null)
        {
            return;
        }

        _remote = new RemoteEntityManager(
            _actorScene,
            _playerComposer,
            _monsterComposer,
            _classRegistry,
            _assetRoot,
            _worldRenderCatalog,
            _log);
        _remote.SetSelfSink(OnSelfSpawn, OnSelfState, OnSelfCombatEvent);
        _remote.NpcMotionStarted += OnNpcMotionStarted;
        _remote.PlayerMotionStarted += OnRemotePlayerMotionStarted;
        _renderer.RequirePreparedTextures();
        _playChannel = channel;
        _reports.SetField(entered.FieldCode);
        _presenceCts = new CancellationTokenSource();
        _presenceReader = Task.Run(() => ReadPresenceAsync(channel, _presenceCts.Token));
    }

    private async Task ReadPresenceAsync(IPlayChannel channel, CancellationToken ct)
    {
        try
        {
            await foreach (var update in channel.ReadAllAsync(ct))
            {
                await _presenceQueue.Writer.WriteAsync(update, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Warn($"[Remote] presence reader ended: {ex.Message}");
        }
    }

    private void DrainPresence(float dt)
    {
        if (_remote == null)
        {
            return;
        }

        if (Interlocked.Exchange(ref _pendingCatalog, null) is { } pending)
        {
            _remote.SetCatalog(pending);
        }

        // Each step is logged and skipped on failure, so one bad update does not hold back the frame's other updates or the
        // remote playback.
        try
        {
            // A warp to a field that was still loading lands once the field is in the world.
            if (_pendingFieldWarp is { } held)
            {
                ApplyWarp(held);
            }
        }
        catch (Exception ex)
        {
            _log.Error($"[Remote] held warp failed: {ex}");
        }

        // Updates apply in arrival order until the queue is empty or the frame's budget is spent, never fewer than a few, so
        // a burst (a crowd entering view, a field's arrival) spreads over frames instead of stalling one. The updates after a
        // warp held for its field wait in the queue until it lands: none is lost, and the new field's entities, removals and
        // drops apply on its ground.
        var started = Stopwatch.GetTimestamp();
        for (var processed = 0;
             _pendingFieldWarp is null
             && (processed < MinPresencePerFrame || Stopwatch.GetElapsedTime(started) < PresenceBudget)
             && _presenceQueue.Reader.TryRead(out var update);
             processed++)
        {
            try
            {
                ApplyPresence(update);
            }
            catch (Exception ex)
            {
                _log.Error($"[Remote] presence update failed: {ex}");
            }

            _playChannel?.Recycle(update);
        }

        try
        {
            _remote.Update(dt);
            _groundLoot.Update(_lootFloor);
        }
        catch (Exception ex)
        {
            _log.Error($"[Remote] playback update failed: {ex}");
        }
    }

    private void ApplyPresence(PresenceUpdate update)
    {
        if (update.Owner is not null)
        {
            Owner = update.Owner;
        }

        _remote.ApplyServer(update);
        if (update.Warp is { } warp)
        {
            ApplyWarp(warp with { Position = WorldSpace.Mirror(warp.Position) });
        }

        _groundLoot.Apply(update);
        ObserveEffects(update);
        ObserveCombatSounds(update);
        ObserveSkills(update);
        ObserveItems(update);
        ObserveProgression(update);
        ObserveRestart(update);
        ObserveWeather(update);
        PresenceObserved?.Invoke(update);
    }

    private void TearDownRemotePresence()
    {
        if (_remote is not null)
        {
            _remote.NpcMotionStarted -= OnNpcMotionStarted;
            _remote.PlayerMotionStarted -= OnRemotePlayerMotionStarted;
        }

        Closing = ClosePresenceAsync(_playChannel, _presenceReader, _presenceCts);
        _remote?.Dispose();
        _remote = null;
        _playChannel = null;
        _presenceReader = null;
        _presenceCts = null;
        _reports.SetField(-1);
        _selfAttacking = false;
        _attackInput.Cancel();
        _targetSelection.Clear();
        _monsterFx?.Clear();
        _renderer?.TransientLights.Clear();
        while (_presenceQueue.Reader.TryRead(out _))
        {
        }
    }

    private async Task ClosePresenceAsync(IPlayChannel channel, Task reader, CancellationTokenSource cancellation)
    {
        try
        {
            if (cancellation is not null)
            {
                await cancellation.CancelAsync();
            }

            if (channel is not null)
            {
                await channel.DisposeAsync();
            }

            if (reader is not null)
            {
                await reader;
            }
        }
        catch (Exception ex)
        {
            _log.Error($"[Remote] asynchronous shutdown failed: {ex}");
        }
        finally
        {
            cancellation?.Dispose();
        }
    }
}
