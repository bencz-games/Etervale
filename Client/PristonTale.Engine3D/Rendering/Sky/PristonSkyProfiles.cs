using PristonTale.Contracts.World;

namespace PristonTale.Engine3D.Rendering;

public readonly record struct PristonSkyProfiles(int Day, int Glow, int Night)
{
    public static readonly PristonSkyProfiles Opening = new(22, 22, 22);
    public static readonly PristonSkyProfiles Forest = new(4, 5, 6);

    // The game's periods (GameHours): the day profile by day, the glow profile in the evening glow, the night profile at
    // night.
    public int Select(float hour)
    {
        if (GameHours.IsNight(hour))
        {
            return Night;
        }

        return hour >= GameHours.GlowStart ? Glow : Day;
    }

    public static int Resolve(string code) => code switch
    {
        "FIELD_BACKIMAGE_RUIN1" => 0,
        "FIELD_BACKIMAGE_RUIN2" => 1,
        "FIELD_BACKIMAGE_GLOWRUIN1" or "FIELD_BACKIMAGE_GLOWRUIN2" => 2,
        "FIELD_BACKIMAGE_NIGHTRUIN1" or "FIELD_BACKIMAGE_NIGHTRUIN2" => 3,
        "FIELD_BACKIMAGE_DAY" => 4,
        "FIELD_BACKIMAGE_GLOWDAY" => 5,
        "FIELD_BACKIMAGE_NIGHT" => 6,
        "FIELD_BACKIMAGE_RAIN" => 7,
        "FIELD_BACKIMAGE_DESERT" => 8,
        "FIELD_BACKIMAGE_GLOWDESERT" => 9,
        "FIELD_BACKIMAGE_NIGHTDESERT" => 10,
        "FIELD_BACKIMAGE_DAYFALL" => 11,
        "FIELD_BACKIMAGE_GLOWFALL" => 12,
        "FIELD_BACKIMAGE_NIGHTFALL" => 13,
        "FIELD_BACKIMAGE_DAYIRON" => 14,
        "FIELD_BACKIMAGE_GLOWIRON" => 15,
        "FIELD_BACKIMAGE_NIGHTIRON1" or "FIELD_BACKIMAGE_NIGHTIRON2" => 16,
        "FIELD_BACKIMAGE_AURORA" => 17,
        "FIELD_BACKIMAGE_SODSKY" => 18,
        "FIELD_BACKIMAGE_SODMOON" => 19,
        "FIELD_BACKIMAGE_SODSUN" => 9,
        "FIELD_BACKIMAGE_SODNONE" => 20,
        "FIELD_BACKIMAGE_IRONBOSS" => 21,
        "FIELD_BACKIMAGE_DAYSNOW" => 23,
        "FIELD_BACKIMAGE_GLOWSNOW" => 24,
        "FIELD_BACKIMAGE_NIGHTSNOW" => 25,
        "FIELD_BACKIMAGE_DAYGREDDY" => 26,
        "FIELD_BACKIMAGE_GLOWGREDDY" => 27,
        "FIELD_BACKIMAGE_NIGHTGREDDY" => 28,
        "FIELD_BACKIMAGE_DAYLOST" => 29,
        "FIELD_BACKIMAGE_GLOWLOST" => 30,
        "FIELD_BACKIMAGE_NIGHTLOST" => 31,
        "FIELD_BACKIMAGE_DAYTEMPLE" => 32,
        "FIELD_BACKIMAGE_GLOWTEMPLE" => 33,
        "FIELD_BACKIMAGE_NIGHTTEMPLE" => 34,
        _ => throw new ArgumentException($"Unknown sky backdrop: {code}", nameof(code))
    };
}
