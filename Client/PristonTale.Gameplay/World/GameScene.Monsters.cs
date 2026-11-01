using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Effects;
using PristonTale.Contracts.Combat;

namespace PristonTale.Gameplay.World;

// Monster visuals: attack, skill, death and body effects that follow each monster's clips, and the statuses monster
// skills put on players and monsters.
public sealed partial class GameScene : IMonsterScene
{
    private readonly List<MonsterActorView> _monsterViews = new();
    private MonsterPresentation _monsterFx;

    public MonsterPresentation MonsterEffects => _monsterFx;

    private void CreateMonsterPresentation()
    {
        if (_monsterFx is null && _skillFx is not null)
        {
            _monsterFx = new MonsterPresentation(new SkillEffectSink(_skillFx), this);
        }
    }

    private void ObserveMonsterSwings(PresenceUpdate update)
    {
        if (_monsterFx is null)
        {
            return;
        }

        foreach (var combatEvent in update.Events)
        {
            if (combatEvent.Kind == CombatEventKind.SwingBegin)
            {
                _monsterFx.ObserveSwing(combatEvent.AttackerId, combatEvent.TargetId);
            }
            else if (combatEvent.Kind == CombatEventKind.Hit && combatEvent.Skill)
            {
                var position = TryGetDrawnPosition(combatEvent.TargetId, out var drawn) ? drawn : Vector3.Zero;
                _monsterFx.ObserveSkillHit(combatEvent.AttackerId, combatEvent.TargetId, position);
            }
        }
    }

    private void UpdateMonsterEffects(float seconds)
    {
        if (_monsterFx is null || _remote is null)
        {
            return;
        }

        _remote.CollectMonsters(_monsterViews);
        _monsterFx.Update(_monsterViews, seconds);
    }

    // A monster's stun holds the player: no walking, attacking or casting until it ends.
    private bool SelfStunned => _skillFx is not null && _remote is not null && MonsterPresentation.Stunned(_skillFx.Statuses.For(_remote.SelfEntityId));

    bool IMonsterScene.TryGetActorMotion(ActorHandle actor, out PristonMotionInfo motion, out float ticks)
    {
        if (_renderer is null)
        {
            motion = null;
            ticks = 0f;
            return false;
        }

        return _renderer.TryGetActorMotion(actor, out motion, out ticks);
    }

    void IMonsterScene.SetEntityTint(ulong entityId, Vector3 tint)
    {
        if (_renderer is null || _remote is null)
        {
            return;
        }

        if (entityId == _remote.SelfEntityId)
        {
            PlayerActors.SetColorAdd(_actorScene, tint);
            return;
        }

        Span<ActorHandle> handles = stackalloc ActorHandle[8];
        var count = _remote.CopyActorHandles(entityId, handles);
        for (var index = 0; index < count; index++)
        {
            _renderer.SetActorColorAdd(handles[index], tint);
        }
    }

    void IMonsterScene.SetEntityCorpse(ulong entityId, float fade, float dim)
    {
        if (_renderer is null || _remote is null)
        {
            return;
        }

        Span<ActorHandle> handles = stackalloc ActorHandle[8];
        var count = _remote.CopyActorHandles(entityId, handles);
        for (var index = 0; index < count; index++)
        {
            _renderer.SetActorCorpse(handles[index], fade, dim);
        }
    }
}
