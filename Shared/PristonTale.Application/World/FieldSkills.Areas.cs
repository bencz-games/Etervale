using System.Numerics;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Skills;

namespace PristonTale.Application.World;

// Area targeting and damage: circles and boxes (with a target cap per rank and accuracy and critical rolls per target),
// chains that jump from the target to the nearest enemy not yet struck, the main target's own share or hit, the
// life-share modes that take a percent of each target's life, and areas that only stun, taunt or push.
internal sealed partial class FieldSkills
{
    // Height a chain link may climb or fall, as the original link search allows.
    private const float ChainHeight = 70f;
    private readonly List<MonsterInstance> _candidates = new();
    private readonly List<WorldPlayer> _echoes = new();

    private static bool Strikeable(MonsterInstance monster) =>
        monster.IsAlive && monster.Def.IsHostile && !monster.IsSummon && !monster.Effects.Has(ControlFlags.Charmed);

    // Whether the monster takes the effect (a boss is spared by an effect that spares bosses).
    private static bool Takes(SkillBuffRule? buff, MonsterInstance monster) => buff is not null && !(buff.SparesBosses && monster.Def.IsBoss);

    // weaponItem: the weapon damage of a party echo (the skill caster's), struck by another player around themselves.
    private int AreaHit(WorldPlayer caster, ActiveCast cast, SkillRule rule, Vector3 aim, (int Min, int Max)? weaponItem = null)
    {
        var rank = cast.Rank;
        var character = caster.Character;
        EndOnAreaAttack(caster);
        _host.TryGetMonster(cast.TargetId, out var main);
        if (main is not null && !Strikeable(main))
        {
            main = null;
        }

        CollectAreaTargets(caster, cast, rule, aim, main);
        var hits = 0;
        if (rule.MainHit is not null && main is not null)
        {
            hits += MainWeaponHit(caster, rule, rank, main) ? 1 : 0;
        }

        var damageRule = rule.Damage;
        if (damageRule is null)
        {
            return hits + AreaEffects(caster, cast, rule);
        }

        var power = damageRule.Mode == SkillDamageMode.Rolled ? SkillFormulas.RollPower(character, rule, damageRule, rank, _rng, weaponItem) : 0;
        var drained = 0;
        foreach (var monster in _targets)
        {
            if (monster is null || !monster.IsAlive || !AreaDamage(caster, cast, rule, monster, power, out var damage, out var crit))
            {
                continue;
            }

            Push(caster, monster, rule.At(rule.PushTo, rank));
            KnockBack(caster, monster, rule.PushBy);
            if (rule.Kind == SkillKind.Area && monster.IsAlive && Takes(cast.Rule.Buff, monster))
            {
                ApplyEffect(monster.Effects, monster.EntityId, caster.EntityId, cast.Rule.Code, cast.Rule.Buff, rank, ResistanceOf(cast.Rule.Buff, monster),
                    character);
            }

            if (damage > 0)
            {
                drained += Math.Min(damage, monster.Life);
                Pay(caster, monster, damage, crit, cast.Rule.ForceFlinch);
            }

            ReportStruck(caster, cast, monster);
            hits++;
        }

        ReturnDrainedLife(caster, damageRule, drained);
        return hits;
    }

    // The targets of an area: a chain, or the enemies inside the circle or box (a capped area takes a random selection).
    // RollsAccuracy skips the enemies that dodge; AreaAccuracy keeps their slot empty, as the original selection counts
    // them. MainInArea adds the clicked target last, beyond the cap and without a roll.
    private void CollectAreaTargets(WorldPlayer caster, ActiveCast cast, SkillRule rule, Vector3 aim, MonsterInstance? main)
    {
        var rank = cast.Rank;
        _targets.Clear();
        var limit = SkillFormulas.At(rule.MaxTargetsByRank, rank, rule.MaxTargets);
        if (rule.AreaShape == SkillAreaShape.Chain)
        {
            CollectChain(caster, cast, limit, rule.At(rule.Radius, rank));
            return;
        }

        var center = rule.AreaCenter == SkillAreaCenter.Caster ? caster.Position : aim;
        var reach = AreaRadius(rule, rank, caster, aim);
        var forward = new Vector2(aim.X - caster.Position.X, aim.Z - caster.Position.Z);
        forward = forward.LengthSquared() > 1e-4f ? Vector2.Normalize(forward) : new Vector2(MathF.Sin(caster.Facing), MathF.Cos(caster.Facing));
        _candidates.Clear();
        foreach (var monster in _host.Monsters)
        {
            if (!Strikeable(monster) || MathF.Abs(monster.Position.Y - center.Y) > HeightTolerance || rule.MainInArea && ReferenceEquals(monster, main))
            {
                continue;
            }

            var offset = new Vector2(monster.Position.X - center.X, monster.Position.Z - center.Z);
            var inside = rule.AreaShape == SkillAreaShape.Box
                ? InBox(offset, forward, rule.BoxWidth * 0.5f, reach)
                : offset.LengthSquared() <= reach * reach;
            if (inside || rule.AreaIncludesTarget && monster.EntityId == cast.TargetId)
            {
                _candidates.Add(monster);
            }
        }

        // A capped area strikes a random selection of the enemies inside it, as the original's rotating pick does.
        if (rule.MaxTargetsByRank.Length == 10)
        {
            for (var index = _candidates.Count - 1; index > 0; index--)
            {
                var swap = _rng.NextExclusive(0, index + 1);
                (_candidates[index], _candidates[swap]) = (_candidates[swap], _candidates[index]);
            }
        }

        var rolls = rule.RollsAccuracy || rule.AreaAccuracy;
        var attack = rolls ? AreaAccuracy(caster.Character, rule, rank) : default;
        foreach (var monster in _candidates)
        {
            if (_targets.Count >= limit)
            {
                break;
            }

            if (rolls && !CombatResolver.RollHit(_rng, CombatResolver.HitChance(attack, DefenderProfile.FromMonster(monster))))
            {
                if (rule.AreaAccuracy)
                {
                    _targets.Add(null);
                }

                continue;
            }

            _targets.Add(monster);
        }

        if (rule.MainInArea && main is not null)
        {
            _targets.Add(main);
        }
    }

    // The chain starts at the target and links to the nearest enemy not yet struck within the link length of the last.
    private void CollectChain(WorldPlayer caster, ActiveCast cast, int links, int linkLength)
    {
        if (!_host.TryGetMonster(cast.TargetId, out var first) || !Strikeable(first))
        {
            return;
        }

        _targets.Add(first);
        var reach = linkLength * (float)linkLength;
        while (_targets.Count < links)
        {
            var last = _targets[^1]!;
            MonsterInstance? nearest = null;
            var best = reach;
            foreach (var monster in _host.Monsters)
            {
                if (!Strikeable(monster) || _targets.Contains(monster) || MathF.Abs(monster.Position.Y - last.Position.Y) > ChainHeight)
                {
                    continue;
                }

                var distance = new Vector2(monster.Position.X - last.Position.X, monster.Position.Z - last.Position.Z).LengthSquared();
                if (distance < best)
                {
                    best = distance;
                    nearest = monster;
                }
            }

            if (nearest is null)
            {
                break;
            }

            _targets.Add(nearest);
        }

        for (var link = 1; link < _targets.Count; link++)
        {
            _skillEvents.Add(new SkillEventView(caster.EntityId, _targets[link]!.EntityId, cast.Rule.Code, SkillEventPhase.Impact, cast.Rank,
                SkillEventFlags.Chain | SkillEventFlags.Area, 0f, 0f, _targets[link - 1]!.Position, 0f, link, linkLength));
        }
    }

    private static AttackProfile AreaAccuracy(Simulation.Characters.PlayerCharacter character, SkillRule rule, int rank)
    {
        var basic = CombatProfileFactory.ForPlayerBasicAttack(character);
        var rating = basic.AttackRating + rule.At(rule.AttackRating, rank);
        return basic with { AttackRating = rating + rating * rule.At(rule.AttackRatingPercent, rank) / 100 };
    }

    // The damage an area target receives; false when the target is skipped (brood and life filters, a failed chance).
    private bool AreaDamage(WorldPlayer caster, ActiveCast cast, SkillRule rule, MonsterInstance monster, int power, out int damage, out bool crit)
    {
        var rank = cast.Rank;
        var damageRule = rule.Damage;
        damage = 0;
        crit = false;
        if (damageRule.OnlyBroods.Length > 0 && !SkillFormulas.MatchesBrood(damageRule.OnlyBroods, monster.Def)
            || damageRule.ExcludeBroods.Length > 0 && SkillFormulas.MatchesBrood(damageRule.ExcludeBroods, monster.Def)
            || damageRule.MinLifePercent > 0 && (long)monster.Life * 100 <= (long)monster.Def.Combat.MaxLife * damageRule.MinLifePercent)
        {
            return false;
        }

        var character = caster.Character;
        var bionic = monster.Def.Combat.Resistances.Bionic;
        switch (damageRule.Mode)
        {
            case SkillDamageMode.MaxLifeShare:
                damage = monster.Def.Combat.MaxLife * Math.Max(0, SkillFormulas.SharePercent(damageRule, rank, bionic)) / 100;
                return damage > 0;
            case SkillDamageMode.CurrentLifeShare:
                if (_rng.RollPercent() >= SkillFormulas.ShareChance(damageRule, rank, character.Level, bionic))
                {
                    return false;
                }

                damage = monster.Life * SkillFormulas.SharePercent(damageRule, rank, bionic) / 100;
                return damage > 0;
        }

        var isMain = monster.EntityId == cast.TargetId;
        var share = power * damageRule.AreaPercent / 100 + (isMain && rule.MainHit is null ? power * damageRule.MainPercent / 100 : 0);
        if (rule.AreaCritical)
        {
            var attack = CombatProfileFactory.ForPlayerBasicAttack(character);
            crit = CombatResolver.RollCrit(_rng, attack, DefenderProfile.FromMonster(monster));
            share = crit ? CombatResolver.ApplyCrit(share, attack.CritBonusPercent) : share;
        }

        if (share > 0)
        {
            var element = isMain ? damageRule.MainElement ?? damageRule.Element : damageRule.Element;
            var resolved = element == damageRule.Element ? damageRule : damageRule with { Element = element };
            damage = SkillFormulas.PowerAgainst(Math.Max(1, share), resolved, monster.Def, character.Effects.Totals, monster.Effects.Totals, rank);
        }

        if (isMain)
        {
            damage = SkillFormulas.WithUndeadBonus(damage, character.Effects.Totals, monster.Def);
            if (damageRule.Main is { } mainRule)
            {
                MainSpellHit(caster, cast, monster, mainRule);
            }
        }

        return true;
    }

    // The main target's own magic hit of an area skill (a javelin's strike under its lightning): rolled with its own rule
    // and a critical chance unless the rule forbids one.
    private void MainSpellHit(WorldPlayer caster, ActiveCast cast, MonsterInstance monster, SkillDamageRule main)
    {
        var character = caster.Character;
        var power = SkillFormulas.RollPower(character, cast.Rule, main, cast.Rank, _rng);
        var crit = false;
        if (!main.NoCritical)
        {
            var attack = CombatProfileFactory.ForPlayerBasicAttack(character);
            crit = CombatResolver.RollCrit(_rng, attack, DefenderProfile.FromMonster(monster));
            power = crit ? CombatResolver.ApplyCrit(power) : power;
        }

        var damage = SkillFormulas.MainSpellPowerAgainst(power, main, cast.Rank, monster.Def, character.Effects.Totals, monster.Effects.Totals);
        Pay(caster, monster, SkillFormulas.WithUndeadBonus(damage, character.Effects.Totals, monster.Def), crit);
    }

    // The main target's weapon-rolled hit of an area skill (MainHit), besides its area share.
    private bool MainWeaponHit(WorldPlayer caster, SkillRule rule, int rank, MonsterInstance main)
    {
        var character = caster.Character;
        var power = SkillFormulas.RollPower(character, rule, rule.MainHit, rank, _rng);
        var damage = SkillFormulas.PowerAgainst(power, rule.MainHit, main.Def, character.Effects.Totals, main.Effects.Totals, rank);
        Pay(caster, main, damage, false, rule.ForceFlinch);
        return true;
    }

    // Areas without damage: control effects, taunts and knock-backs on every selected enemy.
    private int AreaEffects(WorldPlayer caster, ActiveCast cast, SkillRule rule)
    {
        var hits = 0;
        foreach (var monster in _targets)
        {
            if (monster is null || !monster.IsAlive)
            {
                continue;
            }

            var buff = rule.Buff ?? cast.Rule.Buff;
            if (Takes(buff, monster))
            {
                ApplyEffect(monster.Effects, monster.EntityId, caster.EntityId, cast.Rule.Code, buff, cast.Rank, ResistanceOf(buff, monster), caster.Character);
            }

            if (rule.Taunt && !Immovable(monster))
            {
                Taunt(caster, monster, SkillFormulas.TauntDurationMs(rule, cast.Rank, buff));
            }

            KnockBack(caster, monster, rule.PushBy);
            hits++;
        }

        return hits;
    }

    // A party echo (SkillRule.PartyEcho): every member within the ally radius who holds one of the required effects
    // strikes the area around themselves with the caster's weapon.
    private int PartyEcho(WorldPlayer caster, ActiveCast cast, SkillRule rule)
    {
        _echoes.Clear();
        foreach (var member in AlliesAround(caster, rule, cast.Rank))
        {
            if (SkillFormulas.HoldsRequiredEffect(member.Character, rule))
            {
                _echoes.Add(member);
            }
        }

        var weapon = SkillFormulas.WeaponItemDamage(caster.Character);
        var hits = 0;
        foreach (var member in _echoes)
        {
            var struck = AreaHit(member, cast, rule, member.Position, weapon);
            hits += struck;
            _skillEvents.Add(new SkillEventView(member.EntityId, 0, cast.Rule.Code, SkillEventPhase.Impact, cast.Rank, SkillEventFlags.Area, 0f, 0f,
                member.Position, member.Facing, struck, AreaRadius(rule, cast.Rank, member, member.Position)));
        }

        return hits;
    }

    // A rolled single-target spell's drain (SkillDamageRule.Share): a share of the target's maximum life, lowered by its
    // bionic resistance and at most the life it has left, returned to the caster up to the rule's cap. Bosses are spared.
    // lifeBefore: the monster's life before the hit, which the share filters read (MinLifePercent).
    private void DrainAfterHit(WorldPlayer caster, SkillDamageRule damage, int rank, MonsterInstance monster, int lifeBefore)
    {
        if (damage.Mode != SkillDamageMode.Rolled || damage.Share.Length == 0 || !monster.IsAlive || monster.Def.IsBoss && !damage.DrainsBosses
            || damage.OnlyBroods.Length > 0 && !SkillFormulas.MatchesBrood(damage.OnlyBroods, monster.Def)
            || damage.ExcludeBroods.Length > 0 && SkillFormulas.MatchesBrood(damage.ExcludeBroods, monster.Def)
            || damage.MinLifePercent > 0 && (long)lifeBefore * 100 <= (long)monster.Def.Combat.MaxLife * damage.MinLifePercent)
        {
            return;
        }

        var percent = SkillFormulas.SharePercent(damage, rank, monster.Def.Combat.Resistances.Bionic);
        var drained = (int)Math.Min(monster.Life, (long)monster.Def.Combat.MaxLife * percent / 100);
        if (drained <= 0)
        {
            return;
        }

        Pay(caster, monster, drained, false);
        ReturnDrainedLife(caster, damage, drained);
    }

    // An area's struck enemy, for a rule that shows an effect on each.
    private void ReportStruck(WorldPlayer caster, ActiveCast cast, MonsterInstance monster)
    {
        if (cast.Rule.TargetEffect.Length > 0)
        {
            _skillEvents.Add(new SkillEventView(caster.EntityId, monster.EntityId, cast.Rule.Code, SkillEventPhase.Impact, cast.Rank,
                SkillEventFlags.Struck | SkillEventFlags.Area, 0f, 0f, monster.Position, 0f, 1, 0f));
        }
    }

    // Life taken by a drain returns to the caster, capped at a share of the caster's maximum life.
    private void ReturnDrainedLife(WorldPlayer caster, SkillDamageRule? damage, int drained)
    {
        if (damage is null || damage.LifeToCasterCapPercent <= 0 || drained <= 0 || caster.IsDead)
        {
            return;
        }

        HealPlayer(caster, caster, Math.Min(drained, caster.MaxLife * damage.LifeToCasterCapPercent / 100));
    }
}
