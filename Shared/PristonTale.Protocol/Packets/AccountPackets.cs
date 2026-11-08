using PristonTale.Protocol.Binary;
using PristonTale.Contracts.Sessions;
using PristonTale.Contracts.Characters;

namespace PristonTale.Protocol.Packets;

// The protocol a client speaks, sent in its login; a server refuses a client older than its Minimum as outdated. A change
// of any message's layout raises both.
public static class ProtocolVersion
{
    public const uint Current = 25;
    public const uint Minimum = 25;
}

// Account messages: the server greets every connection, the client logs in, manages its characters and keeps the
// connection alive. Bodies start with {uint16 Version = 1}; text is UTF-8 with a uint16 byte length.
public static class AccountWire
{
    public const ushort CurrentVersion = 1;
    public const int MaxAccountBytes = 64;
    public const int MaxPasswordBytes = 128;
    public const int MaxLanguageBytes = 16;
    public const int MaxNameBytes = 64;
}

// S->C on connect {header, uint16 Version = 1, uint16 0, uint32 Protocol, MinimumProtocol, int32 FullPlayers, text
// ServerName (32 B)}: FullPlayers is the number of players in the world when the player limit is reached (then only game
// masters may log in), else 0.
public readonly record struct ServerHelloPacket(uint Protocol, uint MinimumProtocol, int FullPlayers, string ServerName = "")
{
    public const int Size = 8 + 4 + 12;
    public const int MaxServerNameBytes = 32;
    public const int MaxSize = Size + 2 + MaxServerNameBytes;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.ServerHello);
        WireHeader.Write(ref writer, AccountWire.CurrentVersion);
        writer.UInt32(Protocol);
        writer.UInt32(MinimumProtocol);
        writer.Int32(FullPlayers);
        Utf8Text.Write(ref writer, ServerName, MaxServerNameBytes);
        return writer.Finish();
    }

    public static ServerHelloPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.ReadVersion(ref reader, "Server hello");
        reader.Skip(2);
        var hello = new ServerHelloPacket(reader.UInt32(), reader.UInt32(), reader.Int32());
        return reader.Remaining > 0 ? hello with { ServerName = Utf8Text.Read(ref reader, MaxServerNameBytes, "Server hello") } : hello;
    }
}

// C->S {header, uint16 Version = 1, uint16 0, uint32 Protocol, text Account (64 B), text Password (128 B), text Language
// (16 B)}. A login without the language is read only so that a client older than protocol 4 is told it is outdated.
public readonly record struct LoginPacket(uint Protocol, string Account, string Password, string Language) : IFramePacket
{
    public const int MaxSize = 8 + 4 + 4 + 2 + AccountWire.MaxAccountBytes + 2 + AccountWire.MaxPasswordBytes + 2 + AccountWire.MaxLanguageBytes;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.Login);
        WireHeader.Write(ref writer, AccountWire.CurrentVersion);
        writer.UInt32(Protocol);
        Utf8Text.Write(ref writer, Account, AccountWire.MaxAccountBytes);
        Utf8Text.Write(ref writer, Password, AccountWire.MaxPasswordBytes);
        Utf8Text.Write(ref writer, Language, AccountWire.MaxLanguageBytes);
        return writer.Finish();
    }

    public static LoginPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + 4 + 4 + 4);
        WireHeader.ReadVersion(ref reader, "Login");
        reader.Skip(2);
        var protocol = reader.UInt32();
        var account = Utf8Text.Read(ref reader, AccountWire.MaxAccountBytes, "Login");
        var password = Utf8Text.Read(ref reader, AccountWire.MaxPasswordBytes, "Login");
        var language = reader.Remaining > 0 ? Utf8Text.Read(ref reader, AccountWire.MaxLanguageBytes, "Login") : "";
        return new LoginPacket(protocol, account, password, language);
    }
}

// S->C {header, uint16 Version = 1, uint8 Status (LoginFailure, None for a login), uint8 0}; a successful login sends the
// character list first.
public readonly record struct LoginResultPacket(LoginFailure Status)
{
    public const int Size = 8 + 4;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.LoginResult);
        writer.UInt16(AccountWire.CurrentVersion);
        writer.Byte((byte)Status);
        writer.Byte(0);
        return writer.Finish();
    }

    public static LoginResultPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.ReadVersion(ref reader, "Login result");
        return new LoginResultPacket((LoginFailure)reader.Byte());
    }
}

// One character of the account as character select shows it; item codes are 0 for an empty slot, the clan name empty out
// of a clan.
public sealed record CharacterListEntry(string Name, PlayerClass Class, int Level, int Tier, int FaceIndex, int HairIndex, uint WeaponCode, uint ArmorCode,
    uint OffHandCode, bool InVillage, string ClanName = "")
{
    public const int MaxSize = 2 + AccountWire.MaxNameBytes + 4 + 4 + 4 + 12 + 4 + 2 + ClanWire.MaxClanNameBytes;
    private const byte VillageFlag = 1;

    public void Write(ref PacketWriter writer)
    {
        Utf8Text.Write(ref writer, Name, AccountWire.MaxNameBytes);
        writer.UInt32((uint)Class);
        writer.Int32(Level);
        writer.Byte((byte)Math.Clamp(Tier, 0, 255));
        writer.Byte((byte)Math.Clamp(FaceIndex, 0, 255));
        writer.Byte((byte)Math.Clamp(HairIndex, 0, 255));
        writer.Byte(InVillage ? VillageFlag : (byte)0);
        writer.UInt32(WeaponCode);
        writer.UInt32(ArmorCode);
        writer.UInt32(OffHandCode);
        writer.Zero(4);
        Utf8Text.Write(ref writer, ClanName, ClanWire.MaxClanNameBytes);
    }

    public static CharacterListEntry Read(ref PacketReader reader)
    {
        var name = Utf8Text.Read(ref reader, AccountWire.MaxNameBytes, "Character list");
        var playerClass = (PlayerClass)reader.UInt32();
        var level = reader.Int32();
        var tier = reader.Byte();
        var face = reader.Byte();
        var hair = reader.Byte();
        var flags = reader.Byte();
        var weapon = reader.UInt32();
        var armor = reader.UInt32();
        var offHand = reader.UInt32();
        reader.Skip(4);
        var clan = Utf8Text.Read(ref reader, ClanWire.MaxClanNameBytes, "Character list");
        return new CharacterListEntry(name, playerClass, level, tier, face, hair, weapon, armor, offHand, (flags & VillageFlag) != 0, clan);
    }
}

// S->C {header, uint16 Version = 1, uint16 0, int32 Count, entries {text Name, uint32 Class, int32 Level, uint8 Tier,
// Face, Hair, Flags (1 in a village), uint32 WeaponCode, ArmorCode, OffHandCode, uint32 0, text ClanName}}: after login
// and after each change.
public sealed record CharacterListPacket(IReadOnlyList<CharacterListEntry> Characters)
{
    public const int MaxCharacters = CharacterSlots.Maximum;
    public const int MaxSize = 8 + 4 + 4 + MaxCharacters * CharacterListEntry.MaxSize;

    public int Write(Span<byte> destination)
    {
        if (Characters.Count > MaxCharacters)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "A character list holds at most six characters.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.CharacterList);
        WireHeader.Write(ref writer, AccountWire.CurrentVersion);
        writer.Int32(Characters.Count);
        foreach (var character in Characters)
        {
            character.Write(ref writer);
        }

        return writer.Finish();
    }

    public static CharacterListPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + 4 + 4);
        WireHeader.ReadVersion(ref reader, "Character list");
        reader.Skip(2);
        var count = reader.Int32();
        if (count is < 0 or > MaxCharacters)
        {
            throw new InvalidDataException("Character list has an invalid count.");
        }

        var characters = new CharacterListEntry[count];
        for (var index = 0; index < count; index++)
        {
            characters[index] = CharacterListEntry.Read(ref reader);
        }

        if (reader.Remaining != 0)
        {
            throw new InvalidDataException("Character list has trailing bytes.");
        }

        return new CharacterListPacket(characters);
    }
}

public static class CharacterAction
{
    public const byte Create = 1;
    public const byte Delete = 2;
}

// C->S {header, uint16 Version = 1, uint8 Action (1 create, 2 delete), uint8 0, int32 Class, FaceIndex,
// text Name (64 B)}.
public readonly record struct CharacterCommandPacket(byte Action, PlayerClass Class, int FaceIndex, string Name) : IFramePacket
{
    public const int MaxSize = 8 + 4 + 8 + 2 + AccountWire.MaxNameBytes;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.CharacterCommand);
        writer.UInt16(AccountWire.CurrentVersion);
        writer.Byte(Action);
        writer.Byte(0);
        writer.Int32((int)Class);
        writer.Int32(FaceIndex);
        Utf8Text.Write(ref writer, Name, AccountWire.MaxNameBytes);
        return writer.Finish();
    }

    public static CharacterCommandPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + 4 + 8 + 2);
        WireHeader.ReadVersion(ref reader, "Character command");
        var action = reader.Byte();
        reader.Skip(1);
        if (action is not (CharacterAction.Create or CharacterAction.Delete))
        {
            throw new InvalidDataException("Character command has an unknown action.");
        }

        var playerClass = (PlayerClass)reader.Int32();
        var face = reader.Int32();
        return new CharacterCommandPacket(action, playerClass, face, Utf8Text.Read(ref reader, AccountWire.MaxNameBytes, "Character command"));
    }
}

// S->C {header, uint16 Version = 1, uint8 Action, uint8 Ok, int32 Failure (CreateCharacterFailure for a creation),
// text Name}: a successful command sends the new character list first.
public readonly record struct CharacterResultPacket(byte Action, bool Ok, int Failure, string Name)
{
    public const int MaxSize = 8 + 4 + 4 + 2 + AccountWire.MaxNameBytes;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.CharacterResult);
        writer.UInt16(AccountWire.CurrentVersion);
        writer.Byte(Action);
        writer.Byte(Ok ? (byte)1 : (byte)0);
        writer.Int32(Failure);
        Utf8Text.Write(ref writer, Name, AccountWire.MaxNameBytes);
        return writer.Finish();
    }

    public static CharacterResultPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + 4 + 4 + 2);
        WireHeader.ReadVersion(ref reader, "Character result");
        var action = reader.Byte();
        var ok = reader.Byte() != 0;
        var failure = reader.Int32();
        return new CharacterResultPacket(action, ok, failure, Utf8Text.Read(ref reader, AccountWire.MaxNameBytes, "Character result"));
    }
}

// Both directions {header, uint16 Version = 1, uint16 0, uint32 Counter, int64 TimeMs}: the client sends one
// every 4 s; the server echoes it with its own time and closes connections silent for 60 s.
public readonly record struct HeartbeatPacket(uint Counter, long TimeMs) : IFramePacket
{
    public const int Size = 8 + 4 + 4 + 8;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.Heartbeat);
        WireHeader.Write(ref writer, AccountWire.CurrentVersion);
        writer.UInt32(Counter);
        writer.Int64(TimeMs);
        return writer.Finish();
    }

    public static HeartbeatPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.ReadVersion(ref reader, "Heartbeat");
        reader.Skip(2);
        return new HeartbeatPacket(reader.UInt32(), reader.Int64());
    }
}
