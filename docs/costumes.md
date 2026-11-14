# Costume wardrobe

The wardrobe changes the character's visible body without replacing combat armour. Ownership and the active costume
are resolved by the server; a visual mapping is not an equipment permission.

## Read this when

Adding a costume, reviewing purchase or wear restrictions, changing the inventory costume page, or distributing the
costume manifest with the game-data archive.

## Code map

| Area | Repository-relative path |
| --- | --- |
| Exact cosmetic item codes | `Shared/PristonTale.Simulation/Items/CostumeItemPolicy.cs` |
| Gender and optional body mappings | `Shared/PristonTale.Simulation/Items/CostumeCatalog.cs` |
| Wardrobe state and combat equipment | `Shared/PristonTale.Simulation/Characters/PlayerCharacter.cs` |
| Inventory commands | `Shared/PristonTale.Simulation/Items/InventoryCommand.cs` |
| NPC buying rules | `Shared/PristonTale.Simulation/World/Npcs/NpcShopRules.cs` |
| NPC offers and transactions | `Shared/PristonTale.Application/World/NpcServiceTransactions.cs`, `WorldServer.Npc.cs` |
| Character restoration | `Persistence/PristonTale.Persistence.Abstractions/Characters/CharacterStateCodec.cs` |
| Server catalog registration | `Server/PristonTale.Server/Hosting/HostingExtensions.cs` |
| Inventory input, layout and display | `Client/PristonTale.Gameplay/UI/Inventory/HudInventoryController.cs`, `HudInventoryLayout.cs`, `HudInventoryPanel.cs` |
| Visual overrides | `Client/PristonTale.Gameplay/World/Remote/CostumeVisualCatalog.cs`, `EquipmentVisuals.cs` |
| Outfit definitions | `assets/summit/costumes.json` (game-data archive) |
| Tests | `tests/PristonTale.Tests/CostumePolicyIntegrationTests.cs`, `CostumeWardrobeTests.cs`, `EquipmentTests.cs`, `ClientTests.cs` |

## Classification and authorization

The policy uses explicit catalog keys, not a blanket rule for every DA or CA item. The audited catalog distinguishes
64 ordinary armour/robe keys (32 of each) from 92 cosmetic DA keys. It also includes 132 named CA entries, and leaves
24 unidentified CA entries outside the policy. Other item families are not affected. The 224 cosmetic keys include some
formerly combat-capable DA entries: they are intentionally cosmetic under the project rule and do not contribute combat
stats when worn as costumes. Normal armour and robes preserve their combat and class rules.

Cosmetic purchase and wear use the configured gender (`male`, `female`, `any`), not whether a body exists for the class.
The server rejects cosmetic items in ordinary equipment slots. The selected costume is an inventory-backed vanity
attachment rather than a second combat armour. Normal armour continues to determine combat stats.

The shop can present a cosmetic offer originating in the weapon stock under the armour tab without rewriting the
stock database. A goods-tab offer remains under goods. Ordinary weapons are unaffected. The wardrobe page is a
client-side display choice; wear and clear operations remain server-authoritative.

## Manifest format

The version-1 JSON has a `version` integer and `costumes` array. For example:

```json
{
  "version": 1,
  "costumes": [
    {"itemKey": "CA547", "gender": "male", "bodies": {"7": "char/tmABCD/Cmd040.inx"}},
    {"itemKey": "CA548", "gender": "female", "bodies": {"6": "char/tmABCD/Cmc040.inx"}}
  ]
}
```

This is a format example, not the complete production manifest. The class indices are the armour table indices:
0 Mechanician, 1 Fighter, 2 Pikeman, 3 Archer, 4 Knight, 5 Atalanta, 6 Priestess, 7 Magician, 8 Assassin, 9 Shaman,
10 Martial Artist. Each body path is asset-relative, begins with `char/`, and ends with `.inx`. Duplicate keys, invalid
indices, traversal and unsafe paths are invalid. An empty `bodies` object is permitted; normal body presentation is
retained when the active costume has no class-specific mapping. The client resolves body paths using the asset loader.

## Validated outfits

| Outfit | Item keys | Body family | Gender |
| --- | --- | --- | --- |
| Hopy | CA547, CA647 | 040 | Male |
| Cupy | CA548, CA648 | 040 | Female |
| Korean dress | DA148, DA248 | 006 | Female |
| Weddingdress | DA157, DA257 | 013 | Female |
| Chipao | DA159, DA259 | 014 | Female |
| Male Kimono | DA160, DA260 | 015 | Male |
| Female Kimono | DA161, DA261 | 015 | Female |
| Male Korean | DA164, DA264 | 029 | Male |
| Female Korean | DA165, DA265 | 029 | Female |

The manifest contains 224 keys: 64 have body mappings, 160 have none, with 360 associations over 136 distinct
case-insensitive paths. The 18 keys have 96 class-specific associations over 48 distinct INX files. The two CA keys within each pair are item
variants, not separate genders. Family 040 references `man_hopy.tga` for male bodies and `qupy` textures for female
bodies. The current gender rule follows that distinction. Korean family 029 is the selected visual variant; 028 also
exists but the exact original item-to-variant mapping was not established. Changing gender or body definitions is a
manifest change, not an item-code arithmetic rule.

## Inventory hover hint

The wardrobe button uses the original `image/Sinimage/Inven/InfoBox/Costume.bmp` sprite, preloaded through
`HudArt.Static` and drawn at the same logical offset as the weapon-pair hints. The original bitmap is 80 by 27 pixels.
No replacement font, new bitmap, or change to the costume page hit area is necessary.

## How to validate and distribute

1. Preserve the explicit policy and check that the manifest keys match it; do not enable unnamed CA items as a side effect.
2. Resolve each body and its model/texture dependencies from the assets root, retaining exact case on Linux.
3. Build and run the regression runner, then verify purchase, equip/clear, normal armour stats, appearance, movement,
   remote visibility and relogging in the game.
4. Distribute the final manifest and existing HUD art with the separately managed game-data archive. The repository ignores
   `assets/` as a whole; do not force-add the original game binaries or local databases.
5. Restart client and server after installing a manifest so both use the same configuration.

The reviewed manifest is tracked at `tools/costumes/costumes.json`. Install it into the asset archive at
`assets/summit/costumes.json`, preserving any local manifest backup before replacing it. The existing original BMP
is required in the game-data archive; no new binary asset is tracked. See [audit evidence](costumes-audit/README.md)
for classification tables, visual evidence and the expected asset checksums.

```sh
dotnet build tests/PristonTale.Tests -c Release -m:1 -warnaserror
dotnet run --project tests/PristonTale.Tests -c Release --no-build
```

## Pitfalls

- A model file's existence alone does not establish its visual identity. Item-drop meshes are not character bodies.
- Some outfits have no visual mapping; a successful wear is not proof that every costume renders.
- The wardrobe page must not overlap weapon-pair buttons or replace a bag cell.
- Model descriptors, mesh/animation paths and texture names are case-sensitive at the filesystem level on Linux.
- Keep legacy character migration and a full inventory failure path covered by regression tests; never discard an item
  to make a migration succeed.
- The costume page's hover hint should use the same sprite treatment as the equipment-pair hints. Do not replace
  existing equipment sprites or change combat behavior to fix HUD art.

## Related

[Simulation](simulation.md), [client](client.md), [server](server.md), [persistence](persistence.md), [testing](testing.md).
