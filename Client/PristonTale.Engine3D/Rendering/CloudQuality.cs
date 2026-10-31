namespace PristonTale.Engine3D.Rendering;

// The physical sky's volumetric clouds: off (an overcast sky turns grey instead), or their hemisphere at 768 x 192,
// 1024 x 256 or 1536 x 384 texels, a quarter of them marched each frame (PristonPhysicalSky).
public enum CloudQuality
{
    Off,
    Low,
    Medium,
    High
}
