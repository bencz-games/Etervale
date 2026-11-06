using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Characters;

namespace PristonTale.Gameplay.Screens.CharacterSelect;

internal sealed class CharSelectAssets : IDisposable
{
    private const string UiDir = "StartImage/login/CharSelect";
    private const string UiParentDir = "StartImage/login";

    private sealed record JobArtFiles(string NormalLabel, string SelectedLabel, string Description);

    private static readonly Dictionary<PlayerClass, JobArtFiles> JobArtFile = new()
    {
        [PlayerClass.Mechanician] = new("T_cls-meca_.tga", "T_cls-meca.tga", "T_chr-meca.tga"),
        [PlayerClass.Fighter] = new("T_cls-ft_.tga", "T_cls-ft.tga", "T_chr-ft.tga"),
        [PlayerClass.Pikeman] = new("T_cls-pkm_.tga", "T_cls-pkm.tga", "T_chr-pkm.tga"),
        [PlayerClass.Archer] = new("T_cls-ac_.tga", "T_cls-ac.tga", "T_chr-ac.tga"),
        [PlayerClass.Knight] = new("T-cls-knt_.tga", "T-cls-knt.tga", "T_chr-knt.tga"),
        [PlayerClass.Atalanta] = new("T_cls-atlt_.tga", "T_cls-atlt.tga", "T_chr-atlt.tga"),
        [PlayerClass.Priestess] = new("T_cls-prts_.tga", "T_cls-prts.tga", "T_chr-prts.tga"),
        [PlayerClass.Magician] = new("T_cls-mgc_.tga", "T_cls-mgc.tga", "T_chr-mgc.tga"),
        [PlayerClass.Assassin] = new("T_cls-assa_.tga", "T_cls-assa.tga", "T_chr-assa.tga"),
        [PlayerClass.Shaman] = new("T_cls-sha_.tga", "T_cls-sha.tga", "T_chr-sha.tga"),
        [PlayerClass.MartialArtist] = new("T_cls-ma_.tga", "T_cls-ma.tga", "T_chr-ma.tga"),
    };

    private readonly PristonTextureLoader _loader;
    private readonly Func<string, string> _assetPath;
    private readonly Dictionary<PlayerClass, Texture2D> _normalClassLabels = new();
    private readonly Dictionary<PlayerClass, Texture2D> _selectedClassLabels = new();
    private readonly Dictionary<PlayerClass, Texture2D> _classDescriptions = new();
    private readonly Texture2D[,] _faceThumbs = new Texture2D[CharacterFace.FaceCount, 2];

    public CharSelectAssets(GraphicsDevice graphicsDevice, Func<string, string> assetPath)
    {
        _assetPath = assetPath;
        _loader = new PristonTextureLoader(graphicsDevice);
        _loader.Initialize();

        BtOk = Ui("Bt_ok.tga");
        BtSelect = Ui("Bt_select.tga");
        BtBack = Ui("Bt_back.tga");
        BtExit = Ui("Bt_exit.tga");
        BtDelete = Ui("Bt_delet.tga");
        BtNewChar = Ui("bt-Newchar.tga");
        NewCharBig = Ui("New_char.tga");

        FrameL = Ui("mainbox_L.tga");
        FrameT = Ui("mainbox_T.tga");
        FrameR = Ui("mainbox_R.tga");
        FrameD = Ui("mainbox_D.tga");
        FrameCn1 = Ui("mainbox_CN1.tga");
        FrameCn2 = Ui("mainbox_CN2.tga");
        FrameCn3 = Ui("mainbox_CN3.tga");
        FrameCn4 = Ui("mainbox_CN4.tga");

        TitleBox = Ui("title-box.tga");
        TitleTxt = Ui("title-txt30.tga");

        MarkTempscron = Ui("mark_T.tga");
        MarkMorion = Ui("mark_M.tga");
        LogoTempscron = Ui("T-tempscron.tga");
        LogoMorion = Ui("T-Morion.tga");
        RaceBoxMain = Ui("mark_box1.tga");
        RaceBoxEnd = Ui("mark_box2.tga");
        TempscronDescription = Ui("T-ex_tscr.tga");
        MorionDescription = Ui("T-ex_mrin.tga");

        JobBoxMain = Ui("T_cls-box1.tga");
        JobBoxEnd = Ui("T_cls-box2.tga");

        InfoBackPanel = UiParent("UserCharacterInfo.tga");
        InfoOverlay = UiParent("CharacterInfo.tga");

        FaceBigBoxTop = Ui("face_bigbox1.tga");
        FaceBigBoxBottom = Ui("face_bigbox2.tga");
        FaceBoxNormal = Ui("face_box_.tga");
        FaceBoxSelected = Ui("face_box.tga");
        FaceNameBox = Ui("face_box2.tga");

        foreach (var (playerClass, files) in JobArtFile)
        {
            _normalClassLabels[playerClass] = Ui(files.NormalLabel);
            _selectedClassLabels[playerClass] = Ui(files.SelectedLabel);
            _classDescriptions[playerClass] = Ui(files.Description);
        }

        Pixel = new Texture2D(graphicsDevice, 1, 1);
        Pixel.SetData([Color.White]);
    }

    public Texture2D BtOk { get; }
    public Texture2D BtSelect { get; }
    public Texture2D BtBack { get; }
    public Texture2D BtExit { get; }
    public Texture2D BtDelete { get; }
    public Texture2D BtNewChar { get; }
    public Texture2D NewCharBig { get; }

    public Texture2D FrameL { get; }
    public Texture2D FrameT { get; }
    public Texture2D FrameR { get; }
    public Texture2D FrameD { get; }
    public Texture2D FrameCn1 { get; }
    public Texture2D FrameCn2 { get; }
    public Texture2D FrameCn3 { get; }
    public Texture2D FrameCn4 { get; }

    public Texture2D TitleBox { get; }
    public Texture2D TitleTxt { get; }

    public Texture2D MarkTempscron { get; }
    public Texture2D MarkMorion { get; }
    public Texture2D LogoTempscron { get; }
    public Texture2D LogoMorion { get; }
    public Texture2D RaceBoxMain { get; }
    public Texture2D RaceBoxEnd { get; }
    public Texture2D TempscronDescription { get; }
    public Texture2D MorionDescription { get; }
    public Texture2D JobBoxMain { get; }
    public Texture2D JobBoxEnd { get; }

    public Texture2D InfoBackPanel { get; }
    public Texture2D InfoOverlay { get; }
    public Texture2D FaceBigBoxTop { get; }
    public Texture2D FaceBigBoxBottom { get; }
    public Texture2D FaceBoxNormal { get; }
    public Texture2D FaceBoxSelected { get; }
    public Texture2D FaceNameBox { get; }

    public Texture2D Pixel { get; }

    public bool TryClassLabel(PlayerClass playerClass, bool selected, out Texture2D label)
    {
        var labels = selected ? _selectedClassLabels : _normalClassLabels;
        return labels.TryGetValue(playerClass, out label);
    }

    public bool TryClassDescription(PlayerClass playerClass, out Texture2D description)
    {
        return _classDescriptions.TryGetValue(playerClass, out description);
    }

    public Texture2D FaceThumb(int index, bool selected) => _faceThumbs[index, selected ? 1 : 0];

    public void ClearFaceThumbs() => Array.Clear(_faceThumbs);

    public void LoadFaceThumbs(PlayerClass playerClass)
    {
        Array.Clear(_faceThumbs);
        var spec = FaceThumbFiles(playerClass);
        for (var index = 0; index < CharacterFace.FaceCount; index++)
        {
            _faceThumbs[index, 0] = _loader.LoadUiTexture(_assetPath(spec.Dir), spec.Normal[index]);
            _faceThumbs[index, 1] = _loader.LoadUiTexture(_assetPath(spec.Dir), spec.Selected[index]);
        }
    }

    private Texture2D Ui(string name) => _loader.LoadUiTexture(_assetPath(UiDir), name);
    private Texture2D UiParent(string name) => _loader.LoadUiTexture(_assetPath(UiParentDir), name);

    private static (string Dir, string[] Normal, string[] Selected) FaceThumbFiles(PlayerClass playerClass)
    {
        var stem = playerClass switch
        {
            PlayerClass.Mechanician => "TmA",
            PlayerClass.Fighter => "TmB",
            PlayerClass.Pikeman => "TmC",
            PlayerClass.Archer => "TfD",
            PlayerClass.Knight => "MmA",
            PlayerClass.Atalanta => "MfB",
            PlayerClass.Priestess => "MfC",
            PlayerClass.Magician => "MmD",
            PlayerClass.Assassin => "TfE",
            PlayerClass.Shaman => "MmE",
            PlayerClass.MartialArtist => "TfF",
            _ => throw new ArgumentOutOfRangeException(nameof(playerClass), playerClass, "Unsupported character-select class.")
        };
        var morion = playerClass is PlayerClass.Knight or PlayerClass.Atalanta or PlayerClass.Priestess or PlayerClass.Magician or PlayerClass.Shaman;
        var usesLetterSuffix = morion || playerClass is PlayerClass.Assassin or PlayerClass.MartialArtist;
        var order = playerClass == PlayerClass.Atalanta ? new[] { 2, 1, 3 } : new[] { 1, 2, 3 };
        var normalSuffix = usesLetterSuffix ? "-a.tga" : "-01.tga";
        var selectedSuffix = usesLetterSuffix ? "-b.tga" : "-02.tga";
        var normal = new string[CharacterFace.FaceCount];
        var selected = new string[CharacterFace.FaceCount];
        for (var index = 0; index < CharacterFace.FaceCount; index++)
        {
            normal[index] = $"{stem}0{order[index]}{normalSuffix}";
            selected[index] = $"{stem}0{order[index]}{selectedSuffix}";
        }

        return (morion ? UiParentDir + "/Moryon" : UiParentDir, normal, selected);
    }

    public void Dispose()
    {
        Pixel?.Dispose();
        _loader?.Dispose();
    }
}
