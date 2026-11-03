using PristonTale.Protocol.Packets;
using PristonTale.Protocol.Transport;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Server.Net;

// Owner item details and the equipment status values, from the owner state to their extension packets.
public static class ItemWireMapping
{
    public static OwnerItemStatusPacket Status(OwnerState owner)
    {
        var r = owner.Resistances;
        return new OwnerItemStatusPacket(owner.CarriedWeight, owner.MaxWeight, owner.CriticalChance, owner.BlockChance, owner.ShootingRange,
            owner.AttackSpeed, owner.PotionSpace, owner.LifeRegen, owner.ManaRegen, owner.StaminaRegen, owner.MagicMastery,
            [r.Bionic, r.Earth, r.Fire, r.Ice, r.Lighting, r.Poison, r.Water, r.Wind]);
    }

    // The details of every owned item at the owner's inventory revision, in parts that precede its inventory parts.
    public static void SendDetails(OwnerState owner, IPacketSink connection, byte[] scratch)
    {
        var parts = Parts(owner.Items.Select(Detail).ToArray(), ItemDetailsPartPacket.MaxItemsPerPart);
        for (var part = 0; part < parts.Length; part++)
        {
            connection.Send(scratch.AsSpan(0, new ItemDetailsPartPacket(owner.InventoryRevision, part, parts.Length, parts[part]).Write(scratch)));
        }
    }

    // A list sent in numbered parts of at most perPart entries; an empty list is one empty part.
    public static T[][] Parts<T>(T[] items, int perPart) =>
        items.Length == 0 ? [[]] : items.Chunk(perPart).ToArray();

    // An item as a store window shows it (a trade partner's offer, a personal shop, the warehouse).
    public static StoreItemEntry StoreItem(OwnedItemView item) =>
        new(item.InstanceId, item.Code, (byte)item.Kind, item.Count, item.Anchor, (short)item.Width, (short)item.Height, item.Name);

    public static ItemDetailEntry Detail(OwnedItemView item)
    {
        var details = item.Details;
        if (details is null)
        {
            return new ItemDetailEntry { InstanceId = item.InstanceId };
        }

        var roll = details.Roll;
        var spec = roll.Spec;
        var res = roll.Resistances;
        var flags = (details.TwoHanded ? ItemDetailEntry.FlagTwoHanded : 0) | (details.ClassAllowed ? ItemDetailEntry.FlagClassAllowed : 0);
        return new ItemDetailEntry
        {
            InstanceId = item.InstanceId,
            DurabilityCurrent = details.DurabilityCurrent,
            DurabilityMax = details.DurabilityMax,
            UpgradeLevel = details.UpgradeLevel,
            DamageMin = roll.DamageMin,
            DamageMax = roll.DamageMax,
            AttackRating = roll.AttackRating,
            Defence = roll.Defence,
            AddLife = roll.AddLife,
            AddMana = roll.AddMana,
            AddStamina = roll.AddStamina,
            RequiredLevel = roll.RequiredLevel,
            RequiredStrength = roll.Requirements.Strength,
            RequiredSpirit = roll.Requirements.Spirit,
            RequiredTalent = roll.Requirements.Talent,
            RequiredAgility = roll.Requirements.Agility,
            RequiredHealth = roll.Requirements.Health,
            Price = roll.Price,
            Critical = details.Critical,
            AttackSpeed = details.AttackSpeed,
            ShootingRange = details.ShootingRange,
            PotionSpace = details.PotionSpace,
            Weight = details.Weight,
            UniqueItem = details.UniqueItem,
            Flags = flags,
            AgingCount = details.AgingCount,
            AgingGoal = details.AgingGoal,
            MixSheltom = details.MixSheltom,
            MixStats = (int)details.MixStats,
            SpecJob = roll.SpecJob,
            SpecDefence = spec.Defence,
            SpecAttackSpeed = spec.AttackSpeed,
            SpecCritical = spec.Critical,
            SpecShootingRange = spec.ShootingRange,
            SpecLevelLife = spec.LevelLife,
            SpecLevelMana = spec.LevelMana,
            SpecLevelAttackRating = spec.LevelAttackRating,
            SpecLevelDamage = spec.LevelDamage,
            Absorb = roll.Absorb,
            Block = roll.Block,
            MoveSpeed = roll.MoveSpeed,
            LifeRegen = roll.LifeRegen,
            ManaRegen = roll.ManaRegen,
            StaminaRegen = roll.StaminaRegen,
            MagicMastery = details.MagicMastery,
            SpecAbsorb = spec.Absorb,
            SpecMoveSpeed = spec.MoveSpeed,
            SpecBlock = spec.Block,
            SpecMagicMastery = spec.MagicMastery,
            SpecLifeRegen = spec.LifeRegen,
            SpecManaRegen = spec.ManaRegen,
            SpecStaminaRegen = spec.StaminaRegen,
            Resistances = [res.Bionic, res.Earth, res.Fire, res.Ice, res.Lighting, res.Poison, res.Water, res.Wind]
        };
    }
}
