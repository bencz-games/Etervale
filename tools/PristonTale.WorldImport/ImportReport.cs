namespace PristonTale.WorldImport;

// Reconciliation counts produced by an import/verify run — printed at the end.
public sealed class ImportReport
{
    public int SourceFiles { get; set; }
    public int SourceFields { get; set; }
    public int EntityVariants { get; set; }
    public int UnresolvedReferenceRows { get; set; }
    public int ItemFilesParsed { get; set; }
    public int ItemRows { get; set; }
    public int ItemSpecialRows { get; set; }
    public int ItemJobRandomRows { get; set; }

    public int MonsterFilesParsed { get; set; }
    public int MonsterRows { get; set; }
    public int MonsterDropLineRows { get; set; }
    public int MonsterDropItemRows { get; set; }
    public int MonsterDropPlusRows { get; set; }

    public int NpcFilesParsed { get; set; }
    public int NpcRows { get; set; }
    public int NpcShopRows { get; set; }
    public int NpcServiceRows { get; set; }
    public int NpcDialogRows { get; set; }
    public int DialogLineRows { get; set; }

    public int FieldRows { get; set; }
    public int SpawnPointRows { get; set; }
    public int MonsterSpawnRows { get; set; }
    public int BossSpawnRows { get; set; }
    public int FixedNpcRows { get; set; }

    // Motion timing (the INX pass; combat-fidelity F2). Zero when the pass didn't run (no assets root).
    public int MotionModelRows { get; set; }
    public int MotionTableRows { get; set; }
    public int MotionSlotRows { get; set; }
    public int MotionUnresolvedModels { get; set; }

    // Cross-ref resolution.
    public int DropItemCodesResolved { get; set; }
    public int DropItemCodesDangling { get; set; }
    public int ShopItemCodesResolved { get; set; }
    public int ShopItemCodesDangling { get; set; }
    public int SpawnMonsterRefsResolved { get; set; }
    public int SpawnMonsterRefsDangling { get; set; }
    public int FixedNpcRefsResolved { get; set; }
    public int FixedNpcRefsDangling { get; set; }
    public int BossMonsterRefsResolved { get; set; }
    public int BossMonsterRefsDangling { get; set; }

    // Rows of the other languages' texts (Translations/<language>.json).
    public int TranslatedNameRows { get; set; }
    public int TranslatedDialogRows { get; set; }

    public readonly List<string> Warnings = [];
}
