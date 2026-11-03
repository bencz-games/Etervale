namespace PristonTale.Simulation.Items.Usable;

// The icons of the running-effects bar (skills, scrolls, premium items, castle items, the quest timer).
public static class KeepIcons
{
    public const string Folder = "image/Sinimage/skill/keep/";

    // An icon of the folder by file name.
    public static string Path(string fileName) => Folder + fileName;
}
