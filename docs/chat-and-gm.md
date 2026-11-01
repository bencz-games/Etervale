# Chat and game masters

The chat end to end: the chat window and speech bubbles on the client, the chat messages on the wire, the
server's routing of spoken lines, whispers, trade, clan and party lines and notices, and the game-master commands with
their account levels and audit trail. The server decides who hears a line and who may run a command; the client only
composes, shows and limits what the player types.

## Read this when

- You change who hears a line, a channel prefix, a chat limit, a chat colour or the chat window layout.
- You add, remove or change a game-master command or the level it needs.
- You route a new system message (a refusal, a confirmation, a quest hint) to the chat or the notice strip.
- You touch the `gm_level` account column, the `gm_audit` table, `DevAccounts set-gm` or the shutdown countdown.

## Code map

| Path | Responsibility |
| --- | --- |
| `Shared/PristonTale.Simulation/Social/ChatRules.cs` | Limits (78 typed, 70 relayed, 255 per message), spoken range, bubble and flood timings, `Compose`, `Shorten` |
| `Shared/PristonTale.Simulation/Social/ChatFloodGuard.cs` | The flood rules for one speaker, used by the chat input and by the server |
| `Shared/PristonTale.Application/Chat/ChatAppService.cs` | Spoken and party lines, player commands, whispers, the standing notice on heartbeat |
| `Shared/PristonTale.Application/Chat/ChatDirectory.cs` | Players in the world by entity, name and account; level, whisper refusal, notice state |
| `Shared/PristonTale.Application/Chat/ChatLine.cs`, `IChatLink.cs` | `ChatKind`, the line a player receives, the session-side delivery port |
| `Shared/PristonTale.Application/Chat/SocialRosters.cs` | `IPartyRoster` (the world's `PartyRoster` by default), `IClanRoster` and its empty default |
| `Shared/PristonTale.Application/Chat/ChatCommandText.cs` | Word, quoted phrase and number parsing; case-sensitive prefix matching |
| `Shared/PristonTale.Application/GameMasters/GameMasterLevel.cs` | Levels 0 to 4 |
| `Shared/PristonTale.Application/GameMasters/GameMasterCommands.cs` | The command table, `HandleAsync`, the audit entry of every matched command |
| `.../GameMasters/Gm*Commands.cs` | Information, player, item, monster, server and account commands |
| `.../GameMasters/GameMasterServices.cs` | `GameMasterOptions`, `IServerOperations`, item-creation overrides |
| `.../GameMasters/ShutdownCountdown.cs`, `CurrencyLedger.cs`, `GmGifts.cs` | Shutdown notices; gold that left the economy (also logged per period, [application.md](application.md#game-logs)); durable item gifts |
| `Shared/PristonTale.Application/World/WorldServer.Chat.cs`, `WorldServer.GameMasters.cs` | Tick-side reads and changes |
| `Shared/PristonTale.Protocol/Packets/ChatPackets.cs` | The typed line the client sends and the lines it receives |
| `Shared/PristonTale.Contracts/Social/ChatKind.cs` | The kind of a chat line, used by the server, the wire and the chat log |
| `Server/PristonTale.Server/Net/ClientSession.Chat.cs` | `IChatLink` over the session: chat lines, forced warps, close requests |
| `Server/PristonTale.Server/Net/GameServerControl.cs` | `IServerOperations` for the host: user limit, blocked logins, shutdown |
| `Persistence/.../GameMasters/IGameMasterRepository.cs`, `Repositories/DapperGameMasterRepository.cs` | Level, audit, character moves |
| `Client/PristonTale.Gameplay/UI/Chat/HudChat.cs` | Receiving lines, keys, bubble or log, whisper sound, close request |
| `Client/PristonTale.Gameplay/UI/Chat/HudChatWindow.cs`, `HudChatLog.cs` | Window layout, tabs, notice modes, scrolling, colours |
| `Client/PristonTale.Gameplay/UI/Chat/HudChatInput.cs`, `HudChatText.cs` | Input line, history, flood rules, channel prefixes; word wrap |
| `Client/PristonTale.Gameplay/UI/Chat/HudChatBubbles.cs` | Speech bubbles over characters |
| `Client/PristonTale.Gameplay/World/GameScene.Chat.cs` | Sending lines; the head point of an entity for its bubble |

## Wire

Bodies start with `uint16 Version = 1`; text is UTF-8 with a `uint16` byte length (at most 1024 bytes, cut at a whole
character).

| Direction | Message | Body |
| --- | --- | --- |
| Client to server | `ChatSay` `0x45540C01` | `uint8 Party (0 spoken line or command, 1 party), uint8 0, text` |
| Server to client | `ChatLine` `0x45540C00` | `uint8 Kind, uint8 0, uint64 Speaker (0 for server lines), text`: over the speaker when the receiver sees it, else in the log |
| Server to client | `CloseRequest` `0x45540D03` | The client saves nothing more and quits (the shutdown) |

Kinds: 0 server, 1 whisper, 2 system, 3 notice, 4 party, 5 clan, 6 trade, 9 assistant; a player's spoken line carries
`0x80`. The wire carries any character; the input accepts only what the HUD font draws (the single-byte code page).

## Server routing

| Line | Who receives it | Text |
| --- | --- | --- |
| Spoken | Players within 543 units (squared distance under `0x48000`), not the speaker | `Name: text`, cut to 68 characters and `...` over 70 |
| `/:name text`, `/;name text` | Sender and receiver | `To> name: text` and `From> sender: text` |
| `/TRADE> text` | Everybody, the sender included | `Name: text` in the trade kind |
| `/CLAN> text` | Members of the sender's clan | `Name> text` |
| Party chat | The other members of the sender's party | `Name: text` in the party kind |
| `/>text` (Moderator) | Everybody at once | `Notice> text` |
| `/>>text` (Moderator) | Each player in the world, once, with the next heartbeat | `Notice> text`; players who enter later skip it |

- Lines starting with `/` (or `` ` `` and `/`) are commands: never spoken, answered only when a command matches.
- Whisper answers: an empty whisper reports `name is currently online`; a missing player
  `name is currently offline, or cannot be found`; a receiver with `/disable_whisper` gets the sender
  `Unable to send private messages` (`/enable_whisper` undoes it); a receiver that ignores the sender (its friends page)
  gets the sender `Private Message has been refused`.
- `/bosstime` answers `Boss Time: XX:mm` with the minute bosses appear.
- `/도우미` (the original's command) or `/helper` hides the beginner's helper pet or shows it again while the player
  is below level 10 ([application.md](application.md#beginners-helper-pet)); the line is not spoken.
- A line that is only `yahoo`, `/yahoo` or `Yes!` (any case) is not spoken: the player cheers (the Yahoo motion, the
  weapon on the back) from standing, walking or running. For a minute after, Space (`YahooKey`) cheers again for 3% of
  the maximum stamina, refused while stamina is not above that (`YahooRules`). A firecracker cheers after the motion in
  progress, and the tiger capsule cheers at its eating event.
- Party and clan lines go through `IPartyRoster` and `IClanRoster`: the party roster is the world's parties
  ([application.md](application.md#parties)); Etervale has no clan system yet, so the empty clan roster refuses clan
  lines.
- `//party name` (or `//PARTY`) asks that player to group, as the button under a player's portrait does; an unknown
  name answers `name is currently offline, or cannot be found`.
- English builds had no chat profanity filter, so none is applied.

## Client chat window

- Default window at (0,408), 305x210 logical pixels, 12 lines; with the status, skill or inventory window open it
  shrinks to (0,294), 300x116, 8 lines. Lines wrap at 264 pixels; the newest sits at the bottom. The UI font is taller than the
  classic 12-pixel line, so each block shows as many lines as fit its classic area.
- Tabs at the bottom: All, Clan, Party, Trade, Whisper; each holds 64 lines and scrolls back 32 (Page Up, Page Down).
  The notice button cycles the assistant lines: off, a 3-line split above the chat, the assistant page.
- Colours: server (255,128,96), whisper (150,190,255), notice (230,160,255), party (200,255,0), clan (255,255,80),
  trade (200,255,255), assistant (200,128,128), players (255,255,192).
- Enter opens the input or sends; Escape closes the window before anything else; Up and Down recall the last 16
  commands (by first word). The input holds 78 characters. Typing suppresses the HUD and movement keys.
- On the All tab a line is spoken; on the Clan and Trade tabs it is prefixed `/CLAN> ` or `/TRADE> `; on the Party tab,
  or with a leading `@`, it goes as party chat. After a whisper the input keeps `/:name ` for the next line. The classic
  routing marks `~/` (every server) and `` `/ `` (the data server) are dropped before a command is sent, and the server
  drops them too (`ChatRules.WithoutRoute`): one server answers every command.
- Flood rules (`ChatFloodGuard`): more than 10 lines less than 2 s apart freeze the chat for 7 s with an overhead
  complaint; the same spoken line again within 5 s is not sent. The server runs the same guard per member on the lines
  that reach other players (spoken, party, trade, clan; channel lines only count toward the 10 lines) and drops what
  it refuses, so a client that skips the check gains nothing.
- The first whisper or notice while the window is closed opens it and plays the whisper chime.
- Refusals from inventory, skill training, NPC services and item use show in the notice strip; their confirmations and
  skill notices go to the chat as server lines; quest hints and key-binding refusals go to the assistant tab.

## Speech bubbles

A chat message over a character in view (players, NPCs, monsters) becomes a bubble over its head for 5 s (7 s for the
local player) when within 768 units of the camera; a player's line also enters the log. Bubbles wrap at 264 pixels and
12 lines, use the classic chat-box frame with a tail, and colour the speaker's name by kind (players, party and owned
summons, monsters, NPCs); the hovered or selected speaker draws brighter.

## Game masters

The account's `gm_level` (0 Player, 1 Guide, 2 Moderator, 3 Operator, 4 Administrator) is loaded at login into the
session and the chat directory; a higher level may use every command of the levels below. Set it with
`DevAccounts set-gm <user> <level>`; it applies at the next login. Every line that matches a command is written to
`gm_audit` (account, character, level, line, required level, accepted, result), denied ones included, in the background
(`IGmAudit`, `GmAuditQueue`): the command answers at once even while the database is slow or locked; read it with
`DevAccounts gm-audit [count]`. A game master below the level is told the level the command needs
(`> /cmd needs game-master level N (yours is M)`); a player gets no answer.

Commands match by exact, case-sensitive prefix in table order. Levels:

| Level | Commands |
| --- | --- |
| Guide | `/localtime`, `/get_bosstime`, `/get_forceorb`, `/get_primitem`, `/get_currency`, `/get_potion`, `/netlog`, `//netlog` |
| Guide | `/usercode`, `/userid`, `/username`, `/user`, `/sockets`, `/area_users`, `/dm_info`, `/get_guid` |
| Guide | `/specific`, `/Performance`, `/talk`, `/monster`, `/mymonster`, `/event_monster`, `/area_monster` |
| Guide | `/show_blackuser`, `/reset_blackuser`, `/free_blackuser`, `/hide`, `/show`, `/grant` |
| Moderator | `/>`, `/>>`, `/user_limit`, `/kick`, `/kill`, `/open_warehouse`, `/shutdown`, `/weather`, `/insert`, `/near`, `/call`, `/where` |
| Moderator | `/field`, `/player_location` |
| Operator | `/get_money`, `/AgingSetting` |
| Administrator | `/init_premiumitem`, `/@get_perf`, `/@get`, `/@put`, `/GiveMeExp!`, `/expup`, `/GiveMeMoney!`, `/reload_settings`, `/setting` |

- Information: local time, boss minute, the issuer's force orb and premium timers, gold spent per sink, carried potions,
  connection packet counts, players and monsters, a player by entity id, account or name, damage range, session.
- Players: `/kick` disconnects, `/kill` kills, `/near` goes to a player, `/call` brings one, `/where` reports a
  position, `/field n` goes to field n's start point (a random one in the castle), `/player_location x y z` moves within
  the field (each move carries the running statuses), `/hide` and `/show` make the issuer invisible to other players' presence, `/grant` lends the issuer's level to a
  player until that player leaves the world.
- Items: `/@get code` or `/@get "name"` drops an item (`code#U<n>` or a named unique's name makes that named unique,
  `code#Q` the quest variant), `/@get_perf code [class]` its best copy, `/@put name code` writes
  a copy into a player's bag before delivering it, `/AgingSetting code level [class 1-8]` drops a weapon or armour aged
  to the level; `/specific job` and `/Performance value` force the class and middle
  roll of items made in the next minute; `/init_premiumitem n` ends a premium timer.
- Monsters: `/monster name [count]` spawns up to 21 (only the highest level drops items), `/mymonster name [count]` as
  many pets for four minutes (each call adds to the ones still standing; none replaces another or the caller's skill
  summon), `/event_monster name minutes permille` replaces spawns by chance, `/set_absorb id value`, `/talk id text`
  makes an entity speak in a bubble.
- Server: `/user_limit [n]`, the blocked-login list, `/shutdown` (8 minutes: five notices, reminders every 30 s and
  every 5 s in the last minute, then every client is asked to close and the server stops), `/weather
  clear|overcast|rain|storm [1-255]` sets the weather of every field that takes rain (the density, heavy by default)
  until `/weather auto` gives it back to the schedule, and `/weather` tells the weather now.
- Accounts: `/insert account character` moves an offline character to the account's first free slot;
  `/open_warehouse [account]` lists the stored warehouse in the chat.
- Experience: `/GiveMeExp! level` raises to a level, `/expup n` adds n points as a monster's award would.
- `server.test` or `server.debug` (settings table) lower `/GiveMeExp!`, `/expup` and `/GiveMeMoney!` to Guide; on a debug
  server `/` and an item code drops that item; `gm.potion_monitor` (default on) enables `/get_potion`.
- `/reload_settings` reads the settings table at once and lists the refused values; `/setting name` shows a row and its
  meaning, `/setting name value` writes it (refused when the value is not valid) and applies the table.
- Hidden game masters are left out of other players' presence and out of monsters' sight, but they still stand in the
  field: the monsters around one (the ones they call too) do not leave as idle, and no spawn point near one spawns
  (`FieldPreyView.AnyWithin`). A called monster leaves after the same idle time as a spawned one. Game masters have no
  special name colour.

## How to

- Add a game-master command: add a row to `GameMasterCommands.Table` (order matters for shared prefixes), write the
  handler in the matching `Gm*Commands` class, add a case to `GameMasterCommandTests` and the level table above.
- Route a new chat kind: add it to `ChatKind` (Contracts), `HudChatLog` (tab and colour) and the tables above.
- Check live: `DevAccounts set-gm`, start the server on a spare port, then
  `SmokeClient tcp://localhost:<port> <user> <pw> --chat "/localtime"` and `DevAccounts gm-audit`.

## Pitfalls

- Command matching is a prefix match: `/show` must come after `/show_blackuser`, `/@get` after `/@get_perf`.
- The client shows its own spoken and party lines at once; the server never echoes them back.
- The standing notice is delivered on the heartbeat, not on the command.
- Not ported because Etervale has no such system: siege and castle commands, billing and PC-room time, event levels,
  client checksum commands, item-age revision, the wishing fountain, Bellatra (`/sod_view`, `/sod_enter`), billing time (`/time`), couple rings and
  only one game master pet at a time. The classic password elevation is replaced by the stored level.
- The classic client's own commands: `/field` and `/player_location` (and `/expup`) are server commands here, since the
  server owns position and experience; `/save` has no counterpart (every change is stored as it happens); the lighting
  and model debug commands (`/dark`, `/color`, `/time_offset`, `/load`, `/body`, `/mymodel`, ...) and `/latency` are
  not ported, nor is `/camera_wave_on|off`, whose camera wave the client does not have yet.

## Related

- [protocol.md](protocol.md): message framing and opcodes.
- [server.md](server.md): the settings table and the session.
- [client.md](client.md): HUD structure and input.
- [persistence.md](persistence.md): `gm_level`, `gm_audit`.
- [tools.md](tools.md): `DevAccounts set-gm`, `gm-audit`, SmokeClient `--chat`.
- [testing.md](testing.md): chat tests and `--capture-chat`.
