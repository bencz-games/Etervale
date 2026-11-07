namespace PristonTale.Simulation.Social.Castle;

// How the server runs Bless Castle (a server setting): no sieges, a weekly siege, or the open league (sieges one after
// another for CastleRules.LeagueMinutes, each restarting CastleRules.LeagueRestartMinutes after the last ended).
public enum CastleMode : byte
{
    Off = 0,
    Weekly = 1,
    League = 2
}

// Peace, a siege running, or a siege over and closing (CastleRules.OverSeconds) before peace.
public enum SiegeState : byte
{
    Peace = 0,
    Running = 1,
    Over = 3
}

// The owner clan's castle skill: absorption, damage or evasion.
public enum CastleClanSkill : byte
{
    None = 0,
    Absorb = 1,
    Damage = 2,
    Evasion = 3
}

// Bless Castle's rules. A siege lasts SiegeMinutes; the clan whose members dealt the most damage to the Tower of
// Valhalla when it falls takes the castle, and the owner keeps it when the time runs out. Notices go out an hour and
// half an hour before a weekly siege. The owner's tax rate and clan skill take effect each day at TaxHour.
public static class CastleRules
{
    public const int TaxHour = 4;
    public const int SiegeMinutes = 120;
    public const int LeagueMinutes = 120;
    public const int LeagueRestartMinutes = 3;
    public const int OverSeconds = 5;
    public const int FountainMaxPotions = 1440;
    public const int FountainPotionSeconds = 60;
}
