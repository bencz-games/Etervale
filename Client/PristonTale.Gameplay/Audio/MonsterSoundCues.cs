using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Gameplay.Effects;
using PristonTale.Contracts.Units;
using PristonTale.Contracts.Motion;

namespace PristonTale.Gameplay.Audio;

// Monster sounds on the original's cues, from each monster's own animation: its attack or skill sound on the first two
// event frames of the clip, its damage, death or vanishing sound when it enters that motion, and now and then (one time
// in four) its idle sound when it stops. Every client near a monster plays them from the same presence stream.
public sealed class MonsterSoundCues(CombatSounds sounds, IMonsterScene scene)
{
    private const int StandState = (int)MotionCode.Stand;
    private const int AttackState = (int)MotionCode.Attack;
    private const int DamageState = (int)MotionCode.Damage;
    private const int DeadState = (int)MotionCode.Dead;
    private const int SkillState = (int)MotionCode.Skill;
    private const int WarpState = (int)MotionCode.Warp;
    private const int SoundEvents = 2;

    private readonly Dictionary<ulong, Tracked> _tracked = new();
    private readonly HashSet<ulong> _seen = new();
    private readonly List<ulong> _gone = new();

    public void Update(IReadOnlyList<MonsterActorView> monsters, Vector3 listener)
    {
        _seen.Clear();
        foreach (var monster in monsters)
        {
            _seen.Add(monster.EntityId);
            Step(monster, listener);
        }

        _gone.Clear();
        foreach (var id in _tracked.Keys)
        {
            if (!_seen.Contains(id))
            {
                _gone.Add(id);
            }
        }

        foreach (var id in _gone)
        {
            _tracked.Remove(id);
        }
    }

    public void Clear() => _tracked.Clear();

    // A monster entered its warp motion (a wolverine leaving at dawn, a shape change).
    public event Action<MonsterActorView> WarpStarted;

    private void Step(in MonsterActorView monster, Vector3 listener)
    {
        if (!_tracked.TryGetValue(monster.EntityId, out var tracked))
        {
            tracked = new Tracked();
            _tracked.Add(monster.EntityId, tracked);
            sounds.Prepare(monster.SoundCode);
        }

        if (!scene.TryGetActorMotion(monster.Actor, out var motion, out var ticks) || motion is null)
        {
            return;
        }

        var state = (int)motion.State;
        var restarted = tracked.Clip.Advance(motion, ticks);
        if (restarted && state != tracked.State)
        {
            tracked.State = state;
            if (state == WarpState)
            {
                WarpStarted?.Invoke(monster);
            }

            if (Cue(state) is { } cue && (state != StandState || Random.Shared.Next(4) == 0))
            {
                Play(monster, cue, listener);
            }
        }

        if (state is not (AttackState or SkillState))
        {
            return;
        }

        for (var index = 0; index < SoundEvents; index++)
        {
            if (tracked.Clip.Passed(index))
            {
                // A hammer-class monster swings its hammer sound instead of its attack sound.
                Play(monster, state == SkillState ? CharacterSoundCue.Skill : monster.Hammer ? CharacterSoundCue.Hammer : CharacterSoundCue.Attack, listener);
            }
        }
    }

    // Entering a motion: damage, death and vanishing play at once, standing sometimes; attacks and skills wait for their
    // event frames.
    private static CharacterSoundCue? Cue(int state) => state switch
    {
        DamageState => CharacterSoundCue.Damage,
        DeadState => CharacterSoundCue.Dead,
        WarpState => CharacterSoundCue.Warp,
        StandState => CharacterSoundCue.Neutral,
        _ => null
    };

    private void Play(in MonsterActorView monster, CharacterSoundCue cue, Vector3 listener) =>
        sounds.PlayCharacter(monster.SoundCode, cue, monster.Position, listener, monster.Scale);

    private sealed class Tracked
    {
        public ClipEventCursor Clip;
        public int State = -1;
    }
}
