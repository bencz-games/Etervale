namespace PristonTale.Simulation.World;

public static class FieldBackdrops
{
    public static readonly FieldBackdrop Forest = new("FIELD_BACKIMAGE_DAY", "FIELD_BACKIMAGE_GLOWDAY", "FIELD_BACKIMAGE_NIGHT");
    public static readonly FieldBackdrop Ruin1 = new("FIELD_BACKIMAGE_RUIN1", "FIELD_BACKIMAGE_GLOWRUIN1", "FIELD_BACKIMAGE_NIGHTRUIN1");
    public static readonly FieldBackdrop Ruin2 = new("FIELD_BACKIMAGE_RUIN2", "FIELD_BACKIMAGE_GLOWRUIN2", "FIELD_BACKIMAGE_NIGHTRUIN2");
    public static readonly FieldBackdrop Desert = new("FIELD_BACKIMAGE_DESERT", "FIELD_BACKIMAGE_GLOWDESERT", "FIELD_BACKIMAGE_NIGHTDESERT");
    public static readonly FieldBackdrop Fall = new("FIELD_BACKIMAGE_DAYFALL", "FIELD_BACKIMAGE_GLOWFALL", "FIELD_BACKIMAGE_NIGHTFALL");
    public static readonly FieldBackdrop Iron1 = new("FIELD_BACKIMAGE_DAYIRON", "FIELD_BACKIMAGE_GLOWIRON", "FIELD_BACKIMAGE_NIGHTIRON1");
    public static readonly FieldBackdrop Iron2 = new("FIELD_BACKIMAGE_DAYIRON", "FIELD_BACKIMAGE_GLOWIRON", "FIELD_BACKIMAGE_NIGHTIRON2");
    public static readonly FieldBackdrop Snow = new("FIELD_BACKIMAGE_DAYSNOW", "FIELD_BACKIMAGE_GLOWSNOW", "FIELD_BACKIMAGE_NIGHTSNOW");
    public static readonly FieldBackdrop Greedy = new("FIELD_BACKIMAGE_DAYGREDDY", "FIELD_BACKIMAGE_GLOWGREDDY", "FIELD_BACKIMAGE_NIGHTGREDDY");
    public static readonly FieldBackdrop Lost = new("FIELD_BACKIMAGE_DAYLOST", "FIELD_BACKIMAGE_GLOWLOST", "FIELD_BACKIMAGE_NIGHTLOST");
    // A field that names no sky gets the overcast one.
    public static readonly FieldBackdrop Rain = new("FIELD_BACKIMAGE_RAIN", "FIELD_BACKIMAGE_RAIN", "FIELD_BACKIMAGE_RAIN");
    public static readonly FieldBackdrop Temple = new("FIELD_BACKIMAGE_DAYTEMPLE", "FIELD_BACKIMAGE_GLOWTEMPLE", "FIELD_BACKIMAGE_NIGHTTEMPLE");
}
