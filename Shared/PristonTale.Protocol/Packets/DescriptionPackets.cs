using PristonTale.Protocol.Binary;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Social;

namespace PristonTale.Protocol.Packets;

internal static class DescriptionWire
{
    // Version 2 carries the player's shop sign after the fields of version 1; version 3 then the player's clan.
    public const ushort Version = 4;
    public const int MaxNameBytes = 64;
    public const int MaxModelBytes = 160;

    public static void Header(ref PacketWriter writer) => WireHeader.Write(ref writer, Version);

    public static ushort ReadHeader(ref PacketReader reader) => WireHeader.Read(ref reader, "Description message");
}

// C->S: asks for the description of an entity in view. {header, uint16 Version = 2, uint16 Flags = 0, varuint EntityId}.
public readonly record struct DescribeEntityPacket(ulong EntityId) : IFramePacket
{
    public const int MaximumSize = 8 + 4 + VarInt.MaxBytes64;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.DescribeEntity);
        DescriptionWire.Header(ref writer);
        writer.VarUInt(EntityId);
        return writer.Finish();
    }

    public static DescribeEntityPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + 4 + 1);
        DescriptionWire.ReadHeader(ref reader);
        return new DescribeEntityPacket(reader.VarUInt());
    }
}

// A player's looks: class, tier, face, hair, head model, the equipped weapon, armour and off-hand codes, the village
// flag, the Phoenix, the helper pet (0 none), the head look (0 normal, 1-2 big head, 3 soccer ball) and how the held
// weapon and off-hand item glow.
public sealed record DescribedPlayer(
    PlayerClass Class,
    int Tier,
    int Face,
    int Hair,
    string HeadModel,
    uint Weapon,
    uint Armor,
    uint OffHand,
    bool InVillage,
    bool Phoenix,
    int HelperPet,
    int BigHead)
{
    public DescribedGlow WeaponGlow { get; init; } = DescribedGlow.None;
    public DescribedGlow OffHandGlow { get; init; } = DescribedGlow.None;

    // The sign of the personal shop the player sells from; empty when it sells nothing.
    public string ShopSign { get; init; } = "";

    // The player's clan (0 for none), its name and its mark's version (0 the default mark).
    public ulong ClanId { get; init; }
    public string ClanName { get; init; } = "";
    public int ClanMark { get; init; }
    public uint Costume { get; init; }
}

// A held item's glow: {int8 Blink (0 none, below 0 steady), int16 R, G, B, A, float32 Scale, int8 Mix (-1 none), uint8
// MixScroll, uint8 Flags (1 class-up light)}.
public readonly record struct DescribedGlow(int Blink, short R, short G, short B, short A, float Scale, int Mix, int MixScroll, bool ClassUp)
{
    public const int Size = 1 + 8 + 4 + 3;

    public static readonly DescribedGlow None = new(0, 0, 0, 0, 0, 0f, -1, 0, false);

    public void Write(ref PacketWriter writer)
    {
        writer.Byte((byte)(sbyte)Math.Clamp(Blink, sbyte.MinValue, sbyte.MaxValue));
        writer.Int16(R);
        writer.Int16(G);
        writer.Int16(B);
        writer.Int16(A);
        writer.Single(Scale);
        writer.Byte((byte)(sbyte)Math.Clamp(Mix, sbyte.MinValue, sbyte.MaxValue));
        writer.Byte((byte)Math.Clamp(MixScroll, 0, byte.MaxValue));
        writer.Byte((byte)(ClassUp ? 1 : 0));
    }

    public static DescribedGlow Read(ref PacketReader reader)
    {
        var blink = (sbyte)reader.Byte();
        var r = reader.Int16();
        var g = reader.Int16();
        var b = reader.Int16();
        var a = reader.Int16();
        var scale = reader.Single();
        var mix = (sbyte)reader.Byte();
        var scroll = reader.Byte();
        return new DescribedGlow(blink, r, g, b, a, scale, mix, scroll, (reader.Byte() & 1) != 0);
    }
}

// A monster's or NPC's template part: its shared template code and scale, sound code, portrait offsets, brood, the
// owner of a summon or pet with its own scale (0 keeps the template's), and an NPC's service bits.
public sealed record DescribedTemplate(
    uint Template,
    float TemplateScale,
    uint SoundCode,
    int PortraitY,
    int PortraitZ,
    byte Brood,
    ulong Owner,
    float InstanceScale,
    uint NpcServices,
    int Class = 0,
    int ShadowSize = 0);

// S->C: what a viewer needs to draw an entity, for one description revision. {header, uint16 Version = 2, uint16 Flags =
// 0, varuint EntityId, uint8 Revision, uint8 Kind, varuint Level, varuint MoveSpeed, float32 AttackRate, text Name, text
// Model}, then for a player {varuint Class, Tier, Face, Hair, text HeadModel, uint32 Weapon, Armor, OffHand, uint8 Flags (1
// village, 2 Phoenix), uint8 HelperPet, uint8 BigHead, glow WeaponGlow, glow OffHandGlow (DescribedGlow), text ShopSign
// (from version 2)}, else {varuint Template, float32 TemplateScale, varuint SoundCode, varint PortraitY, PortraitZ,
// uint8 Brood, varuint Owner, float32 InstanceScale, uint32 NpcServices, varuint Class (the monster class: 1 boss, 200
// hammer), varint ShadowSize (-1 none, 0 small to 3 large)}. Text is UTF-8 with a uint16 byte length.
public sealed record EntityDescriptionPacket(
    ulong EntityId,
    byte Revision,
    EntityKind Kind,
    int Level,
    int MoveSpeed,
    float AttackRate,
    string Name,
    string Model,
    DescribedPlayer? Player = null,
    DescribedTemplate? Template = null)
{
    // The player part (four varuints, the head model, three codes, three bytes, two glows, the sign, the clan) is the larger tail.
    private const int PlayerPart = 5 * 4 + 2 + DescriptionWire.MaxModelBytes + 4 * 3 + 3 + 2 * DescribedGlow.Size + 2 + PersonalShopLimits.MaxSignBytes
        + VarInt.MaxBytes64 + 2 + ClanWire.MaxClanNameBytes + 5 + 4;
    public const int MaximumSize = 8 + 4 + VarInt.MaxBytes64 + 2 + 5 * 2 + 4 + (2 + DescriptionWire.MaxNameBytes) + (2 + DescriptionWire.MaxModelBytes)
        + PlayerPart;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.EntityDescription);
        DescriptionWire.Header(ref writer);
        writer.VarUInt(EntityId);
        writer.Byte(Revision);
        writer.Byte((byte)Kind);
        writer.VarUInt((ulong)Math.Max(0, Level));
        writer.VarUInt((ulong)Math.Max(0, MoveSpeed));
        writer.Single(AttackRate);
        NpcWire.Text(ref writer, Name, DescriptionWire.MaxNameBytes);
        NpcWire.Text(ref writer, Model, DescriptionWire.MaxModelBytes);
        if (Kind == EntityKind.Player)
        {
            var player = Player ?? throw new InvalidOperationException("A player description needs its looks.");
            writer.VarUInt((ulong)Math.Max(0, (int)player.Class));
            writer.VarUInt((ulong)Math.Max(0, player.Tier));
            writer.VarUInt((ulong)Math.Max(0, player.Face));
            writer.VarUInt((ulong)Math.Max(0, player.Hair));
            NpcWire.Text(ref writer, player.HeadModel, DescriptionWire.MaxModelBytes);
            writer.UInt32(player.Weapon);
            writer.UInt32(player.Armor);
            writer.UInt32(player.OffHand);
            writer.Byte((byte)((player.InVillage ? 1 : 0) | (player.Phoenix ? 2 : 0)));
            writer.Byte((byte)Math.Clamp(player.HelperPet, 0, byte.MaxValue));
            writer.Byte((byte)Math.Clamp(player.BigHead, 0, byte.MaxValue));
            player.WeaponGlow.Write(ref writer);
            player.OffHandGlow.Write(ref writer);
            NpcWire.Text(ref writer, player.ShopSign, PersonalShopLimits.MaxSignBytes);
            writer.VarUInt(player.ClanId);
            NpcWire.Text(ref writer, player.ClanName, ClanWire.MaxClanNameBytes);
            writer.VarUInt((ulong)Math.Max(0, player.ClanMark));
            writer.UInt32(player.Costume);
        }
        else
        {
            var template = Template ?? throw new InvalidOperationException("A monster or NPC description needs its template.");
            writer.VarUInt(template.Template);
            writer.Single(template.TemplateScale);
            writer.VarUInt(template.SoundCode);
            writer.VarInt(template.PortraitY);
            writer.VarInt(template.PortraitZ);
            writer.Byte(template.Brood);
            writer.VarUInt(template.Owner);
            writer.Single(template.InstanceScale);
            writer.UInt32(template.NpcServices);
            writer.VarUInt((uint)Math.Max(0, template.Class));
            writer.VarInt(Math.Clamp(template.ShadowSize, -1, 3));
        }

        return writer.Finish();
    }

    public static EntityDescriptionPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + 4 + 1);
        var version = DescriptionWire.ReadHeader(ref reader);
        var entityId = reader.VarUInt();
        var revision = reader.Byte();
        var kind = (EntityKind)reader.Byte();
        var level = reader.VarUInt31();
        var moveSpeed = reader.VarUInt31();
        var attackRate = reader.Single();
        var name = NpcWire.Text(ref reader, DescriptionWire.MaxNameBytes);
        var model = NpcWire.Text(ref reader, DescriptionWire.MaxModelBytes);
        switch (kind)
        {
            case EntityKind.Player:
            {
                var playerClass = (PlayerClass)reader.VarUInt31();
                var tier = reader.VarUInt31();
                var face = reader.VarUInt31();
                var hair = reader.VarUInt31();
                var head = NpcWire.Text(ref reader, DescriptionWire.MaxModelBytes);
                var weapon = reader.UInt32();
                var armor = reader.UInt32();
                var offHand = reader.UInt32();
                var flags = reader.Byte();
                var pet = reader.Byte();
                var bigHead = reader.Byte();
                var weaponGlow = DescribedGlow.Read(ref reader);
                var offHandGlow = DescribedGlow.Read(ref reader);
                var sign = version >= 2 ? NpcWire.Text(ref reader, PersonalShopLimits.MaxSignBytes) : "";
                var clanId = version >= 3 ? reader.VarUInt() : 0UL;
                var clanName = version >= 3 ? NpcWire.Text(ref reader, ClanWire.MaxClanNameBytes) : "";
                var clanMark = version >= 3 ? reader.VarUInt31() : 0;
                var costume = version >= 4 ? reader.UInt32() : 0u;
                return new EntityDescriptionPacket(entityId, revision, kind, level, moveSpeed, attackRate, name, model,
                    new DescribedPlayer(playerClass, tier, face, hair, head, weapon, armor, offHand, (flags & 1) != 0, (flags & 2) != 0, pet, bigHead)
                    {
                        WeaponGlow = weaponGlow,
                        OffHandGlow = offHandGlow,
                        ShopSign = sign,
                        ClanId = clanId,
                        ClanName = clanName,
                        ClanMark = clanMark,
                        Costume = costume
                    });
            }
            case EntityKind.Monster or EntityKind.Npc:
            {
                var template = reader.VarUInt32();
                var templateScale = reader.Single();
                var sound = reader.VarUInt32();
                var portraitY = reader.VarInt();
                var portraitZ = reader.VarInt();
                var brood = reader.Byte();
                var owner = reader.VarUInt();
                var instanceScale = reader.Single();
                var services = reader.UInt32();
                var monsterClass = reader.VarUInt31();
                var shadow = reader.VarInt();
                return new EntityDescriptionPacket(entityId, revision, kind, level, moveSpeed, attackRate, name, model, null,
                    new DescribedTemplate(template, templateScale, sound, portraitY, portraitZ, brood, owner, instanceScale, services, monsterClass,
                        shadow));
            }
            default:
                throw new InvalidDataException($"Description of unknown kind {(int)kind}.");
        }
    }
}
