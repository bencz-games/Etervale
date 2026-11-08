using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Combat;
using PristonTale.Simulation.Skills;

namespace PristonTale.Gameplay.Effects;

// The bursts of players' basic hits and the blood of players hit. A player's basic hit flashes the critical burst with a
// strong red light on a critical, the weapon's spark with a faint one otherwise and the bare spark, unlit, when the
// player holds no weapon; a holder of an enchant (SkillRule.HitEffect) shows its element's hit instead, and a thrower of
// a shot effect (SkillRule.ShotEffect) none, its shot ending in its own. A player losing life to any hit bleeds 16 above
// its feet with a red light.
public sealed class HitEffects(SkillPresentation skills)
{
    public const string Critical = "vfx.hit.critical";
    public const string Normal = "vfx.common.normalhit";
    public const string Bare = "vfx.hit.bare";
    public const string Blood = "vfx.hit.blood";

    private static readonly Func<SkillRule, string> HitEffectOf = static rule => rule.HitEffect;
    private static readonly Func<SkillRule, string> ShotEffectOf = static rule => rule.ShotEffect;

    // weaponOf: a player's held weapon (empty for bare hands), null for anything else; position: where an entity stands.
    public void Observe(PresenceUpdate update, Func<ulong, string> weaponOf, Func<ulong, Vector3?> position)
    {
        var events = update.Events;
        for (var index = 0; index < events.Count; index++)
        {
            var hit = events[index];
            if (hit.Kind != CombatEventKind.Hit)
            {
                continue;
            }

            if (hit.Damage > 0 && weaponOf(hit.TargetId) is not null && position(hit.TargetId) is { } bleeding)
            {
                skills.Spawn(Blood, hit.TargetId, hit.TargetId, bleeding);
            }

            if (!hit.Skill && weaponOf(hit.AttackerId) is { } weapon && position(hit.TargetId) is { } struck
                && skills.StatusEffect(hit.AttackerId, ShotEffectOf).Length == 0)
            {
                var name = skills.StatusEffect(hit.AttackerId, HitEffectOf) is { Length: > 0 } enchant ? enchant
                    : weapon.Length == 0 ? Bare : hit.Critical ? Critical : Normal;
                skills.Spawn(name, hit.AttackerId, hit.TargetId, struck);
            }
        }
    }
}
