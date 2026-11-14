# GM class-tier testing

`/unlocktier [1..5]` is an Administrator (GM level 4) command in game chat. It acts on the issuer's
online character, through the normal character-save gate, and is audited by `gm-audit`.

The command uses visible tier numbers 1..5. The character records these as tier indices 0..4:

| Command | Internal tier | Highest level before the next tier |
| --- | ---: | ---: |
| `/unlocktier 1` | 0 | 19 |
| `/unlocktier 2` | 1 | 39 |
| `/unlocktier 3` | 2 | 59 |
| `/unlocktier 4` | 3 | 79 |
| `/unlocktier 5` | 4 | 285 (the current game maximum) |

With an explicit tier the command raises the character, if necessary, to the bracket's highest level.
It never lowers an existing level, experience, tier, learned skill rank or proficiency. Skills available at
that level within the selected tier are learned at rank 1 if not already learned. Higher-level skills
remain unlearned when not eligible; the skill window itself may already show their icons.

Without a number, `/unlocktier` selects the highest visible tier available at the current level from
`RankUpRules.RequiredLevel` (20, 40, 60, 80). It does **not** raise the level and learns only skills
whose `RequiredLevel` is met. The GM bypass does not complete class quests or award their quest rewards.
This is for test characters, not production progression.

Example for a freshly created Priestess:

1. Sign in with an account that has GM level 4, create the Priestess and enter the world.
2. Enter `/unlocktier 4` in chat; this moves an eligible new character to level 79 and learns all 16
   first-four-tier skills, including Chain Lightning.
3. For a level-based unlock instead, use `/GiveMeExp! 70` then `/unlocktier`.
4. To inspect Tier 5, use `/unlocktier 5` and note that the game maximum is currently level 285.

The command modifies only its own character and preserves skill usage and inventory, money, and points.
It uses `ICharacterSaves` rather than directly editing SQLite. Recompile and restart the server after
applying the patch. A source-only change is not verified until the Release build and regression runner pass.
