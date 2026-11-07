using System.Numerics;
using PristonTale.Simulation.Items;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Texts;

namespace PristonTale.Simulation.World;

// A stone the wing warp map lists: its order on the map, the field it lands in, its name, the spot of its 16 x 16 icon
// on the 256 x 256 map, the help document shown while the pointer is on it and the level a trip there asks (the field's
// own limit applies as well).
public sealed record WingGateDestination(int Index, int FieldCode, string Name, int MapX, int MapY, string Document, int Level = 0);

// The wing warp gates. Stepping on a warp stone (a FieldWarpGate with SpecialEffect 2) opens a map of the stones a
// carried wing reaches: without a wing Ricarten and Pillai, with one the first Reach stones of the list, the best wing
// carried counting; a member of the clan holding Bless Castle also sees the castle's stone, whatever wing it carries.
// Ricarten, Pillai and the field the player stands in are free; elsewhere the trip costs the wing's fee. The traveller
// lands on the exit of the destination's stone.
public static class WingGates
{
    // A warp gate's special effect: a delayed warp with the return sparkle, or a wing warp stone.
    public const int DelayedEffect = 1;
    public const int StoneEffect = 2;
    public const int FreeReach = 2;
    public const int FreeDestinations = 2;

    public const string MapArt = "image/Sinimage/help/WG_map.tga";
    public const string IconArt = "image/Sinimage/help/WG_icon.tga";
    public const string SelectArt = "image/Sinimage/help/WG_slt.tga";
    public const string DocumentFolder = "image/Sinimage/WarpGateDoc";
    public const string IntroDocument = "Default";

    public static readonly IReadOnlyList<WingGateDestination> Destinations =
    [
        new(0, 3, "Ricarten", 96, 199, "Ricarten"),
        new(1, 21, "Pillai", 64, 21, "Pilai"),
        new(2, 18, "Land of Dusk", 88, 84, "Forest2"),
        new(3, 1, "Bamboo Forest", 67, 177, "Forest3"),
        new(4, 6, "Ruinen Village", 78, 116, "Ruinen"),
        new(5, 9, "Navisko Town", 150, 165, "Nevisco"),
        new(6, 12, "Forbidden Land", 158, 137, "Desert4", 55),
        new(7, 29, "Eura Village", 155, 65, "yura", 90),
        new(8, 37, "Land of Chaos", 140, 225, "chaoticpost", 95)
    ];

    // Bless Castle's stone, for the owner clan's members only.
    public static readonly WingGateDestination Castle = new(Destinations.Count, FieldCodes.Castle, "Bless Castle", 69, 207, "B_Castle");

    // Every stone the map may show, the castle's last.
    public static readonly IReadOnlyList<WingGateDestination> All = [.. Destinations, Castle];

    // The wings from Metal to Extreme: how many stones each reaches and the fee it charges.
    public static readonly IReadOnlyList<WingGateWing> Wings =
    [
        new(ItemCodes.Parse("QW101"), "Metal Wing", 4, 100),
        new(ItemCodes.Parse("QW102"), "Silver Wing", 5, 300),
        new(ItemCodes.Parse("QW103"), "Gold Wing", 6, 500),
        new(ItemCodes.Parse("QW104"), "Diamond Wing", 7, 1000),
        new(ItemCodes.Parse("QW105"), "Chaos Wing", 8, 2000),
        new(ItemCodes.Parse("QW106"), "Extreme Wing", 9, 4000)
    ];

    public static bool IsWing(uint code)
    {
        foreach (var wing in Wings)
        {
            if (wing.Code == code)
            {
                return true;
            }
        }

        return false;
    }

    // The best wing among the carried item codes; null without one.
    public static WingGateWing BestWing(IEnumerable<uint> carried)
    {
        WingGateWing best = null;
        foreach (var code in carried)
        {
            foreach (var wing in Wings)
            {
                if (wing.Code == code && (best is null || wing.Reach > best.Reach))
                {
                    best = wing;
                }
            }
        }

        return best;
    }

    public static int Reach(WingGateWing wing) => wing?.Reach ?? FreeReach;

    public static int RequiredLevel(WingGateDestination destination, int fieldLimit) => Math.Max(destination.Level, fieldLimit);

    public static bool TryGet(int index, out WingGateDestination destination)
    {
        destination = index >= 0 && index < All.Count ? All[index] : null;
        return destination is not null;
    }

    // Whether the map offers the stone to a player with this wing, a member of the castle's owner clan or not.
    public static bool Offers(WingGateDestination destination, WingGateWing wing, bool castleOwner) =>
        ReferenceEquals(destination, Castle) ? castleOwner : destination.Index < Reach(wing);

    // What a trip to the destination costs a player with this wing standing in the given field, with the castle's tax.
    public static long Fee(WingGateWing wing, WingGateDestination destination, int currentField, int tax = 0) =>
        wing is null || destination.Index < FreeDestinations || destination.FieldCode == currentField ? 0 : Social.Castle.CastleRules.WithTax(wing.Fee, tax);

    public static bool IsStone(FieldWarpGate warp) => warp.SpecialEffect == StoneEffect;

    // The line the player reads for a trip that did not warp.
    public static GameText Message(WingGateStatus status) => status == WingGateStatus.Warped ? GameText.None : GameText.Of(status switch
    {
        WingGateStatus.NotAtStone => GameTextId.GateNotAtStone,
        WingGateStatus.NotReached => GameTextId.GateNotReached,
        WingGateStatus.LevelTooLow => GameTextId.LevelTooLow,
        WingGateStatus.NotEnoughGold => GameTextId.NotEnoughGold,
        WingGateStatus.Busy => GameTextId.ActionPending,
        WingGateStatus.StorageFailed => GameTextId.CharacterUnchanged,
        _ => GameTextId.GateRefused
    });

    // True when the position (original X and Z) stands on a stone of the field.
    public static bool AtStone(FieldDef field, Vector3 position)
    {
        foreach (var warp in field.WarpGates)
        {
            var radius = warp.ServerReach;
            var delta = new Vector2(position.X - warp.X, position.Z - warp.Z);
            if (IsStone(warp) && delta.LengthSquared() <= radius * radius)
            {
                return true;
            }
        }

        return false;
    }

    // Where a trip to the field lands: the exit of its stone, else its first start point (original X, Y, Z).
    public static Vector3 Exit(FieldDef field)
    {
        foreach (var warp in field.WarpGates)
        {
            if (IsStone(warp))
            {
                return new Vector3(warp.DestinationX, warp.DestinationY, warp.DestinationZ);
            }
        }

        var start = field.Arrival;
        return new Vector3(start.X, 0f, start.Z);
    }
}

public sealed record WingGateWing(uint Code, string Name, int Reach, long Fee);
