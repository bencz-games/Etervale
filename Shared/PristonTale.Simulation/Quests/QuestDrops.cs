using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Ai;

namespace PristonTale.Simulation.Quests;

public enum QuestDropSource : byte
{
    // The monster's main drop table: quest items reach only first-tier characters (and the vamp the second tier).
    MainTable = 0,

    // The monster's independent bonus drops.
    BonusTable = 1,

    // The monster's event item, rolled on every kill.
    EventItem = 2
}

// A quest or ingredient item a kill hands to the credited player; it reaches the pack in the next quest upkeep.
public readonly record struct PendingQuestDrop(uint Code, int Count, int MonsterCode, QuestDropSource Source);

// Which quest items a player receives from kills, and the upkeep that stores them together with the quest changes the
// world tick cannot make itself: weapon growth, expired quest items and leftovers of finished steps.
public static class QuestDrops
{
    public static bool PassesSource(PlayerCharacter character, uint code, QuestDropSource source) =>
        source != QuestDropSource.MainTable || character.Tier == 0 || character.Tier == 1 && code == QuestItems.Vamp;

    // The royal amulet drops 21 times in 100, and only before the fourth tier; every other event item drops on every kill.
    public static bool EventItemDrops(ISimRng rng, uint code, int creditedTier) =>
        code != QuestItems.RoyalAmulet || creditedTier < RoyalAmuletTiers && rng.RollPercent() <= 20;

    private const int RoyalAmuletTiers = 3;

    // Quest items reach the pack only while a running quest wants them, one copy (or the counted number) at a time.
    public static bool Accepts(QuestContext context, uint code, int monsterCode)
    {
        var active = context.Journal.Active;
        var character = context.Character;
        if (code == QuestItems.LostHammer)
        {
            return active.Code == QuestCode.ElementaryG && !QuestInventory.Has(character, code);
        }

        if (code == QuestItems.Honey || code == QuestItems.Oil)
        {
            return RankUpByPotion.Accepts(context, code);
        }

        if (QuestItems.IsIngredient(code))
        {
            return false;
        }

        if (RankUpByTrophies.IsTrophy(code))
        {
            return RankUpByTrophies.Accepts(context, code);
        }

        if (code == QuestItems.Vamp)
        {
            return RankUpByQuestWeapon.Accepts(context, code);
        }

        if (code == QuestItems.BonusVampOne || code == QuestItems.BonusVampTwo || code == QuestItems.BonusVampThree)
        {
            return active.Code == QuestCode.Level80Bonus && !QuestInventory.Has(character, code);
        }

        if (code == QuestItems.RoyalAmulet)
        {
            return RankUpByMasterTrial.Accepts(context, code, monsterCode);
        }

        if (code == QuestItems.RoyalJelly || code == QuestItems.HairTonic)
        {
            return active.Code is QuestCode.RankUpHoney or QuestCode.RankUpOil && !QuestInventory.Has(character, code);
        }

        return true;
    }

    // Runs on a candidate copy: stores the pending drops, grows or expires the quest weapon and clears leftovers.
    public static void Upkeep(QuestContext context, IReadOnlyList<PendingQuestDrop> drops)
    {
        var character = context.Character;
        foreach (var drop in drops)
        {
            if (PassesSource(character, drop.Code, drop.Source) && Accepts(context, drop.Code, drop.MonsterCode))
            {
                // A full pack loses the item, as the classic client dropped a quest item it could not place.
                QuestInventory.TryGrant(context, [new QuestGrant(drop.Code, Math.Max(1, drop.Count))]);
            }
        }

        RemoveLeftovers(context);
        EliteQuests.Upkeep(context);
        RankUpByQuestWeapon.Grow(context);
        RankUpByQuestWeapon.Expire(context);
    }

    private static readonly uint[] FirstRankUpItems =
    [
        QuestItems.SilverWolverineTrophy, QuestItems.BronzeWolverineTrophy, QuestItems.GoldenWolverineTrophy, QuestItems.RoyalJelly,
        QuestItems.HairTonic
    ];

    private static void RemoveLeftovers(QuestContext context)
    {
        var character = context.Character;
        var active = context.Journal.Active;
        if (active.Code != QuestCode.ElementaryG)
        {
            QuestInventory.Remove(character, QuestItems.LostHammer);
        }

        if (active is { Code: QuestCode.Level85, State: 1 })
        {
            QuestInventory.Remove(character, QuestItems.KhaliaTear);
        }

        if (character.Tier >= 1)
        {
            foreach (var code in FirstRankUpItems)
            {
                QuestInventory.Remove(character, code);
            }
        }

        if (character.Tier >= 2 && active.Code != QuestCode.RankUpQuestWeapon)
        {
            QuestInventory.Remove(character, QuestItems.Vamp);
        }

        if (active.Code != RankUpByHestian.Code)
        {
            QuestInventory.Remove(character, QuestItems.MoonStone);
            QuestInventory.Remove(character, QuestItems.GemOfHestian);
        }
    }

    // Whether an upkeep has work besides pending drops; checked by the tick every few seconds and after kills and deaths.
    public static bool UpkeepDue(PlayerCharacter character, long nowUnixMs)
    {
        var active = character.Quests.Active;
        if (EliteQuests.OwesItems(active))
        {
            return true;
        }

        if (active is { Code: QuestCode.RankUpQuestWeapon, State: 3, WeaponKillsLeft: <= 0 } && active.WeaponStage is >= 1 and < RankUpByQuestWeapon.FinalStage)
        {
            return true;
        }

        if (active is { Code: QuestCode.RankUpQuestWeapon, State: 5 } && character.Tier >= 2
            && (RankUpByQuestWeapon.Expired(character.Quests, nowUnixMs)
                || !QuestInventory.Holds(character, active.WeaponId) && !QuestInventory.Holds(character, active.RingId)))
        {
            return true;
        }

        if (active.Code != QuestCode.ElementaryG && QuestInventory.Has(character, QuestItems.LostHammer))
        {
            return true;
        }

        if (active is { Code: QuestCode.Level85, State: 1 } && QuestInventory.Has(character, QuestItems.KhaliaTear))
        {
            return true;
        }

        if (character.Tier >= 1)
        {
            foreach (var code in FirstRankUpItems)
            {
                if (QuestInventory.Has(character, code))
                {
                    return true;
                }
            }
        }

        return character.Tier >= 2 && active.Code != QuestCode.RankUpQuestWeapon && QuestInventory.Has(character, QuestItems.Vamp);
    }
}

// Quest work done on the world tick against the live character: kill credit, deaths and timers. These change only the
// journal; item changes they cause wait for the next quest upkeep.
public static class QuestTick
{
    // Returns true when the kill finished a stage of the quest weapon, which the upkeep then grows, or earned an elite
    // quest's item, which the upkeep hands out.
    public static bool OnKill(QuestContext context, int monsterCode)
    {
        if (monsterCode == QuestMonsters.EventMonster || context.Journal.Active.IsIdle)
        {
            return false;
        }

        LevelQuests.OnKill(context, monsterCode);
        LevelBonusQuests.OnKill(context, monsterCode);
        ElementaryQuests.OnKill(context, monsterCode);
        FuryOfPhantomQuest.OnKill(context);
        RankUpByMasterTrial.OnKill(context, monsterCode);
        RankUpByHestian.OnKill(context, monsterCode);
        var eliteItem = EliteQuests.OnKill(context, monsterCode);
        return RankUpByQuestWeapon.OnKill(context, monsterCode) | eliteItem;
    }

    // Returns true when the death took a quest item back, which the upkeep then removes.
    public static bool OnDeath(QuestContext context)
    {
        var takeBack = LevelQuests.OnDeath(context);
        LevelBonusQuests.OnDeath(context);
        return takeBack;
    }

    public static void Step(QuestContext context, long elapsedMs)
    {
        if (context.Journal.Active.IsIdle)
        {
            return;
        }

        RankUpByMasterTrial.StepTimer(context, elapsedMs);
        RankUpByHestian.StepTimer(context, elapsedMs);
        EliteQuests.StepTimer(context, elapsedMs);
        RankUpByQuestWeapon.CheckLevelSkip(context);
    }
}
