# Costume audit evidence

This directory retains the reviewed classification, visual associations and verification record for the wardrobe.
The runtime contract and maintenance procedure are in [Costume wardrobe](../costumes.md).

## Classification and verification

The read-only item catalog audit matched 64 ordinary DA entries (32 armour and 32 robe) by names, required level
and defence. Two additional magical variants lacked a matching local item. The initial positional matching was
incorrect and its apparent stat discrepancies were discarded. The remaining 92 DA keys are cosmetic under the
project rule, including 18 previously combat-capable entries documented in the initial classification summary.
Of 156 CA keys, 132 named costume keys were included. CA101–CA124 remain deferred because their names were absent.
Other item families, including Hopy crystals, are outside the policy.

The final manifest contains 224 keys, 64 with body mappings and 160 with no override. There are 360 body associations
across 136 distinct case-insensitive INX paths. The 18 targeted keys have 96 associations across 48 INX paths. The
absence of a body is deliberately not an authorization failure.

## Model investigation

The first filename inventory was insufficient: item meshes and monster names produced false positives, and a narrow
suffix search missed older body variants. The expanded audit examined 412 local and 2068 external INX candidates.
Reading model descriptors and mesh texture references resolved 40 initial mappings for Wedding, Chipao and Kimono.
Further inspection found the `qupy` texture spelling in the female 040 bodies and `man_hopy` in male 040 bodies;
Hopy and Cupy use the same family with gender restrictions inferred from that evidence, not from the CA prefix.
The Archer wedding texture uses the spelling `ar_weding`. The final mappings use 006, 013, 014, 015, 029 and 040.
The Korean 028 and 029 families both exist; 029 is the selected, game-tested variant, not a proven original code match.
All 18 targeted items were reported working in game. No external binary asset was needed for these mappings.

The shop audit used read-only SQLite stock tab values. Cosmetic offers in Weapon are displayed in Armor; Goods
remains Goods and ordinary weapon sellers were reported unaffected. The database was not changed. The inventory
hover label uses the existing original Costume BMP, rather than separately drawn text.

## Evidence files

- `classification-initial.csv`: original 224-key classification and body coverage; not the final gender mapping.
- `classification-summary-initial.json`: initial counts, combat-capable exceptions and deferred CA keys.
- `classification-final.csv`: initial and final gender/body coverage for each key.
- `visual-evidence.json`: relative INX, mesh and texture evidence for the 18 targeted keys, including Hanbok alternatives.
- `SHA256SUMS-assets.txt`: expected final manifest and existing HUD bitmap hashes relative to the installation root.
- `../../tools/costumes/costumes.json`: reviewed manifest; install as `assets/summit/costumes.json` in the game data.

## Verification record

Release build with warnings as errors succeeded after the HUD change. The cosmetic filter passed 6/6 and the costume
filter passed 11/11 (filters can overlap). `git diff --check` showed no output. The HUD screenshot check, complete
suite and isolated-commit build are not established by these filtered results. The 160 model-less items remain
an acknowledged coverage gap, not a failed classification. Do not include unrelated changes or private databases.
