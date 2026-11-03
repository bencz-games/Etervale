namespace PristonTale.Assets;

// The SMD binary layout every reader shares. A file opens with a 24-byte signature inside its file header; a stage then
// holds its stage header record (the area grid of AreaWidth^2 cells, then the counts), the material group and its
// materials, the vertices and the faces.
public static class SmdLayout
{
    public const string StageSignature = "SMD Stage data Ver 0.72";
    public const string StageSignature73 = "SMD Stage data Ver 0.73";
    public const string ModelSignature = "SMD Model data Ver 0.62";
    public const string StagePrefix = "SMD Stage data";
    public const string ModelPrefix = "SMD Model data";
    public const int SignatureSize = 24;

    public const int FileHeaderSize = 556;
    public const int StageHeaderSize = 262260;
    public const int AreaWidth = 256;
    public const int MaterialGroupSize = 88;
    public const int MaterialSize = 320;
    public const int StageVertexSize = 28;
    public const int StageFaceSize = 28;
}
