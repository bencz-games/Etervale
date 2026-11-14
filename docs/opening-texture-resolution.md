# Opening cinematic texture resolution (Fedora recovery)

The opening stage `assets/StartImage/Opening/Map/stage.smd` contains original material
names such as `fg_1_04.bmp`, `riy-w017.bmp`, `tem_wall05.BMP`, `eye_ground01.BMP`,
`pl_03.TGA` and `sea_0.BMP`. On the Fedora checkout these textures were absent
from `StartImage/Opening/Map` but present in `assets/Field/Ricarten`. Copies with
the same name in `assets/Field/castle` often have **different SHA-256 hashes**;
several different castle names even share the same content hash. Choosing a
random global basename match would silently change the opening art.

`OpeningCinematic` therefore explicitly configures the renderer to try
`assets/Field/Ricarten` **only for materials whose source directory is**
`assets/StartImage/Opening/Map`. The normal texture resolver takes precedence:
case-insensitive local names and alternate texture extensions still work.
When neither directory contains a texture, the existing missing-texture warning
and white fallback remain. No files are copied or overwritten and the general
field/NPC renderer is unchanged. This mapping is optional: absent Ricarten
assets do not make the client fail to load.

The opening NPC E source `StartImage/Opening/npc/opening_NPC_E.smd` has material
names `TN-00-h.BMP`, `TN-006-b.BMP`, `mm.bmp`, and `npc-tt.BMP`. The original
`char/npc/TN-006/TN-006.smd` independently references `mm.bmp`; the original
`char/npc/TN-006/mm.bmp` is 32x64 and differs from `Field/Sod/mm.bmp` (256x256).
Consequently the opening NPC folder gets a **name-specific, optional** fallback
for `mm.bmp` only, to `char/npc/TN-006`. All other NPC materials still resolve
in their own folder; other opening NPCs cannot inherit arbitrary TN-006 names.
An opening-local `mm.bmp` always takes precedence. No assets are copied.
This correspondence is grounded in shared authored material names, not a
comparison of image appearance. The `wav/Talk` voice archive is unrelated.

## Check on Fedora

From the repository root, after applying the patch:

```bash
dotnet build tests/PristonTale.Tests -c Release -m:1 -warnaserror
dotnet run --project tests/PristonTale.Tests -c Release --no-build
bash scripts/run-client.sh
```

Compare the opening log to the original warnings. The `StartImage/Opening/Map`
missing-texture warnings for textures available under `Field/Ricarten` should
disappear; any remaining missing resources should be reported individually.
The test runner checks that a local opening texture wins, the explicitly chosen
Ricarten file is preferred to a homonymous castle file, extension fallback
still works, missing files remain missing, and only `mm.bmp` can use the original
TN-006 folder as an NPC-specific fallback.
