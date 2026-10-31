namespace PristonTale.Engine3D.Rendering;

// The physical sky's volumetric clouds: off (an overcast sky turns grey instead), or their hemisphere rendered at 256,
// 384 or 512 texels a side every frame.
public enum CloudQuality
{
    Off,
    Low,
    Medium,
    High
}
