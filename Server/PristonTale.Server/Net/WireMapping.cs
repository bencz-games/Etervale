using PristonTale.Application.Services;
using PristonTale.Application.World;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Loot;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Sessions;
using PristonTale.Contracts.Units;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Server.Net;

// Server-side mapping from the authoritative views to Etervale's wire records.
internal static class WireMapping
{

    public static uint ItemCode(string? catalogKey) => ItemCodes.TryParse(catalogKey, out var code) ? code : 0;

    // The template key clients use to share one description among monsters and NPCs of a kind. Players and owned
    // summons describe themselves (a summon's owner and size are its own), so they carry none.
    public static uint SharedTemplate(in EntityView view) =>
        view.Kind == EntityKind.Player || view.OwnerId != 0 ? 0u : PresenceWire.TemplateKey(view.Kind, view.TemplateId);


    private static DescribedGlow Glow(in Simulation.Items.HeldItemGlow glow) =>
        new(glow.Blink, glow.R, glow.G, glow.B, glow.A, glow.Scale, glow.Mix, glow.MixScroll, glow.ClassUp);

    // An entity's description for one revision (the answer to DescribeEntity).
    public static EntityDescriptionPacket Describe(in EntityView view, byte revision, WorldData data)
    {
        switch (view.Kind)
        {
            case EntityKind.Player:
            {
                var appearance = view.Appearance;
                var looks = new DescribedPlayer(appearance.Class, appearance.Tier, appearance.FaceIndex, appearance.HairIndex, appearance.ModelName2 ?? "",
                    ItemCode(appearance.WeaponKey), ItemCode(appearance.ArmorKey), ItemCode(appearance.OffHandKey), appearance.InVillage, appearance.Phoenix,
                    appearance.HelperPet, appearance.BigHead)
                {
                    WeaponGlow = Glow(appearance.WeaponGlow),
                    OffHandGlow = Glow(appearance.OffHandGlow),
                    ShopSign = appearance.ShopSign,
                    ClanId = (ulong)Math.Max(0, appearance.ClanId),
                    ClanName = appearance.ClanName,
                    ClanMark = appearance.ClanMark,
                    Costume = ItemCode(appearance.CostumeKey)
                };
                return new EntityDescriptionPacket(view.EntityId, revision, EntityKind.Player, view.Level, view.MoveSpeed, view.AttackRate,
                    appearance.Name ?? "", appearance.ModelName ?? "", looks);
            }
            case EntityKind.Monster when data.Monsters.TryGet((int)view.TemplateId, out var monster):
            {
                // An owned summon fights on its owner's side: the original marks it with the user brood.
                var owned = view.OwnerId != 0;
                var template = new DescribedTemplate(view.TemplateId, monster.ModelScale, (uint)monster.SoundCode, monster.PortraitOffsetY,
                    monster.PortraitOffsetZ, (byte)(owned ? Simulation.World.Monsters.MonsterBrood.User : monster.Brood), view.OwnerId,
                    owned ? view.ModelScale : 0f, 0, (int)monster.Class, monster.ShadowSizeLevel);
                return new EntityDescriptionPacket(view.EntityId, revision, EntityKind.Monster, view.Level, view.MoveSpeed, view.AttackRate,
                    monster.Name ?? "", monster.ModelInxPath ?? "", Template: template);
            }
            case EntityKind.Npc when data.Npcs.TryGet((int)view.TemplateId, out var npc):
            {
                var template = new DescribedTemplate(view.TemplateId, npc.ModelScale, (uint)npc.SoundCode, npc.PortraitOffsetY, npc.PortraitOffsetZ, 0, 0, 0f,
                    npc.ServiceSet.Bits, 0, npc.ShadowSizeLevel);
                return new EntityDescriptionPacket(view.EntityId, revision, EntityKind.Npc, view.Level, view.MoveSpeed, view.AttackRate,
                    npc.Label ?? "", npc.ModelInxPath ?? "", Template: template);
            }
            default:
                // A template missing from the catalog is still described by kind and code.
                return new EntityDescriptionPacket(view.EntityId, revision, view.Kind,
                    view.Level, view.MoveSpeed, view.AttackRate, "", "", Template: new DescribedTemplate(view.TemplateId, 1f, 0, 0, 0, 0, view.OwnerId, 0f, 0));
        }
    }

    // Character info record embedded in the character record for the entering character.
    public static CharacterListEntry Entry(CharacterSummary summary) => new(
        summary.Name,
        summary.Class,
        summary.Level,
        summary.Tier,
        summary.FaceIndex,
        summary.HairIndex,
        ItemCode(summary.WeaponKey),
        ItemCode(summary.ArmorKey),
        ItemCode(summary.OffHandKey),
        summary.InVillage,
        summary.ClanName) { CostumeCode = ItemCode(summary.CostumeKey) };

    public static OwnerStatusPacket Status(OwnerState owner) => new(
        owner.Level, owner.Experience, owner.LevelStart, owner.NextLevel, owner.Money,
        owner.Life, owner.MaxLife, owner.Mana, owner.MaxMana, owner.Stamina, owner.MaxStamina,
        owner.StatPoints, owner.SkillPoints, owner.Strength, owner.Spirit, owner.Talent, owner.Agility, owner.Health,
        owner.DamageMin, owner.DamageMax, owner.Defence, owner.Absorption, owner.AttackRating, owner.MoveSpeed, owner.AttackRange,
        owner.InventoryRevision, owner.ProgressionRevision, owner.SkillsRevision, owner.EliteSkillPoints, owner.Tier, (byte)owner.BagPage,
        (byte)owner.WeaponSet, owner.CostumeItemId, ItemCode(owner.CostumeKey));

    public static InventoryItemEntry Item(OwnedItemView item) => new(
        item.InstanceId, item.Code, (int)item.Kind, item.Count, item.Anchor, item.Slot, (short)item.Width, (short)item.Height, item.Name, item.Link);

    public static CombatEventEntry Event(in CombatEventView view)
    {
        byte flags = 0;
        if (view.Crit)
        {
            flags |= CombatEventFlags.Critical;
        }

        if (view.Killed)
        {
            flags |= CombatEventFlags.Killed;
        }

        if (view.Skill)
        {
            flags |= CombatEventFlags.Skill;
        }

        return new CombatEventEntry(view.AttackerId, view.TargetId, view.Kind, flags, view.DurationMs, view.HitAtMs, view.Damage,
            view.Resources, view.SourceSoundCode, GameUnits.ToFixed(view.Position.X), GameUnits.ToFixed(view.Position.Y),
            GameUnits.ToFixed(view.Position.Z), (short)GameUnits.ToAngle(view.Facing), (short)Math.Clamp(view.Clip, -1, short.MaxValue));
    }

    public static SkillEventEntry SkillEvent(in SkillEventView view) => new(
        view.CasterId,
        view.TargetId,
        view.SkillCode,
        view.Phase,
        (byte)Math.Clamp(view.Rank, 0, 255),
        view.Flags,
        view.DurationMs,
        view.HitAtMs,
        GameUnits.ToFixed(view.Position.X),
        GameUnits.ToFixed(view.Position.Y),
        GameUnits.ToFixed(view.Position.Z),
        (short)GameUnits.ToAngle(view.Facing),
        (ushort)Math.Clamp(view.TargetCount, 0, ushort.MaxValue),
        (int)MathF.Round(view.Radius));

    public static StatusEffectEntry Status(in StatusEventView view)
    {
        ushort flags = 0;
        if (view.Harmful)
        {
            flags |= StatusEffectFlags.Harmful;
        }

        if (view.Permanent)
        {
            flags |= StatusEffectFlags.Permanent;
        }

        var duration = view.Permanent ? 0 : Math.Max(0, view.DurationMs);
        var remaining = view.Permanent ? 0 : Math.Clamp(view.RemainingMs, 0, duration);
        return new StatusEffectEntry(view.EntityId, view.SourceId, view.StatusCode, view.Op, (byte)Math.Clamp(view.Stacks, 0, 255),
            view.SourceKind, view.Control, flags, duration, remaining, view.Value);
    }

    public static GroundItemEntry Drop(GroundDropView drop) => new(
        drop.Id,
        drop.Gold ? ItemCodes.GoldCode : drop.ItemCode,
        drop.Gold,
        drop.Amount,
        GameUnits.ToFixed(drop.Position.X),
        GameUnits.ToFixed(drop.Position.Y),
        GameUnits.ToFixed(drop.Position.Z),
        drop.Name);
}
