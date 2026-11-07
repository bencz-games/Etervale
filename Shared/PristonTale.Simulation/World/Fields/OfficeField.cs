using PristonTale.Simulation.World;

namespace PristonTale.Simulation.World.Fields;

// The game room, far from every field and without a map of its own.
public static class OfficeField
{
    public static readonly FieldDef Definition = new(
        "office", "Office", FieldState.Room, FieldBackdrops.Ruin2,
        "Field\\Room\\office.smd", [])
    {
        FieldCode = FieldCodes.OfficeRoom,
        Center = new FieldPoint(-200000, 200000),
        BackMusicCode = "BGM_CODE_FOREST",
        ServerCode = "PLAY_SERVER_CODE_MAIN"
    };
}
