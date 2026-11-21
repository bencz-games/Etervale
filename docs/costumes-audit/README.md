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

The current manifest contains 224 keys, 68 with body mappings and 156 with no override. There are 384 body
associations across 148 distinct case-insensitive INX paths. The 22 documented keys have 120 associations
across 60 INX paths. The absence of a body is deliberately not an authorization failure.

## Model investigation

The first filename inventory was insufficient: item meshes and monster names produced false positives, and a narrow
suffix search missed older body variants. The expanded audit examined 412 local and 2068 external INX candidates.
Reading model descriptors and mesh texture references resolved 40 initial mappings for Wedding, Chipao and Kimono.
Further inspection found the `qupy` texture spelling in the female 040 bodies and `man_hopy` in male 040 bodies;
Hopy and Cupy use the same family with gender restrictions inferred from that evidence, not from the CA prefix.
The Archer wedding texture uses the spelling `ar_weding`. The final mappings use 006, 013, 014, 015, 029 and 040.
The Korean 028 and 029 families both exist; 029 is the selected, game-tested variant, not a proven original code match.
The original 18 targeted items were reported working in game. The four Tuxedo/Changpao variants were subsequently
mapped to masculine families 013/014, with INX model names and SMD texture names checked using AssetDump; both
outfits were observed working in game. This is not a claim that all six male classes were individually tested.
No external binary asset was needed for these mappings.

The shop audit used read-only SQLite stock tab values. Cosmetic offers in Weapon are displayed in Armor; Goods
remains Goods and ordinary weapon sellers were reported unaffected. The database was not changed. The inventory
hover label uses the existing original Costume BMP, rather than separately drawn text.

## Evidence files

- `classification-initial.csv`: original 224-key classification and body coverage; not the final gender mapping.
- `classification-summary-initial.json`: initial counts, combat-capable exceptions and deferred CA keys.
- `classification-final.csv`: initial and final gender/body coverage for each key.
- `visual-evidence.json`: relative INX, mesh and texture evidence for the 22 documented keys, including Hanbok alternatives.
- `SHA256SUMS-assets.txt`: expected final manifest and existing HUD bitmap hashes relative to the installation root.
- `../../tools/costumes/costumes.json`: reviewed manifest; install as `assets/summit/costumes.json` in the game data.

## Combat contribution correction

The original wardrobe implementation excluded costumes from the equipment totals. The revised contract adds the
active, server-authorized costume's rolled bonuses to the ordinary gear totals without using an armour slot. A visual
mapping is not required for the bonus. Regression coverage checks additive defence, absorption, resources, damage,
class specialization, switching and clearing, bag-page changes, persistence, removal and denied authorization.
No extra assets, database record changes or new bonus values are required by this correction.

## Verification record

Release build with warnings as errors succeeded for the original wardrobe changes. The cosmetic filter passed
6/6 and the costume filter passed 11/11 (filters can overlap). For the Tuxedo/Changpao visual update, the
full regression run on 2026-11-21 reported 885/887 passing. The failures concerned two NPC model descriptors
(`kadomatsu.ini`, `zizzyadorii.ini`) and a missing revive/restart skill effect in the local game data; neither
failure names a costume mapping. Baseline reproduction of these failures was not independently established.
The 156 model-less items remain an acknowledged coverage gap, not a failed classification. Do not include
unrelated changes or private databases.
