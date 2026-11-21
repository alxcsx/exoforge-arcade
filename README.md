# Exoforge Arcade — samples

Each sample is a tiny game whose design gives one set of Exoforge capabilities a reason to exist.
The server half is plain C# extensions (shared, in `extensions/`); the client half is one Unity scene in its own Unity project. All samples share one
cluster, so they also show extensions reacting to each other: coins from one game show up in
another, and achievements unlock from all of them.

| # | Game | Project / scene | Extensions | Capabilities it shows |
| :- | :--- | :--- | :--- | :--- |
| 0 | **Main Menu** of a mock game | `2d-hub` / `MainMenu` | `game_menu` | Hub for every sample · player-readable config · per-player settings · **debugging walkthrough** (rid → timeline → capture → replay) |
| 1 | **Brainrot Exchange** — idle meme stock market | `2d-hub` / `BrainrotExchange` | `wallet`, `daily_rewards`, `brainrot_market` | Extension → extension calls through generated stubs · server-computed idle income · `[ExoCron]` price ticks broadcast to everyone · Studio-edited live-ops tables · `OnExtensionStart` seeding · `[ExoWebhook]` storefront purchase |
| 2 | **Echo Dungeon** — top-down PvE MMO adventure | `2d-echo-dungeon` / `EchoDungeon` | `adventure_world`, `achievements` | A trusted game server reporting through a `service`-scoped action · world events broadcast to all players · `OnEvent` listeners with no client calls · achievements unlocked by events from *every* sample |
| 3 | **Snowball Siege** — 3D world-boss FPS | *separate Unity project* | `world_boss` (pays through `wallet`) | Shared state many players change at once · trusted hit reports · cron respawn · exactly-once payouts across services |
| 4 | **Doodle Dash** — draw-your-level platformer | `2d-doodle-dash` / `DoodleDash` | `level_share` | File bucket uploads with metadata · browse, play and rate player-made content · the same catalog over plain HTTP |
| 5 | **Item Shop** — 2D shop and inventory | `2d-shop` / `Shop` | `items`, `inventario`, `loja_itens` | Three extensions with one job each · `loja_itens` orchestrates a purchase through `items` and `inventario` · foreign-key resources (`inventario_itens.item_id` → `itens.id`) · Studio-seeded catalog and offers · server-owned gold |

**Rules every sample follows**

- **Gameplay never holds a client or a token.** Game code raises events; one bridge component per
  scene is the only thing calling `ExoServices.*`. Delete the bridge and the game still runs offline.
- **Snake-sized.** Primitive shapes, IMGUI, no art assets, gameplay code about the size of Snake.
- **Exoforge is not the netcode.** In the MMO and the FPS, movement and combat belong to a dedicated
  game server (an HLAPI-style server in a real game). Exoforge receives *trusted results* from it —
  kills, hits, chests — and owns what persists: progress, rewards, achievements, the boss. In the
  samples that game server is a clearly labelled mock, and other players appear in the feeds, not
  as avatars.

---

## 0. Main Menu — the hub, and where things go wrong on purpose

**The game.** The title screen of a mock game, "Exoforge Arcade": a news banner, the player's name
and coins, a settings panel (volume, colour-blind mode), and one button per sample. One more
button, *Claim Gift*, fails for a reason you have to find.

| Extension | Actions | Resources |
| :--- | :--- | :--- |
| `game_menu` | `get_settings`, `save_settings`, `claim_gift` (player) | `menu_news` (player-readable, edited in Studio), `player_settings` |

The news banner reads `menu_news` directly as a player-readable resource, with no action written for
it. Name and coins come from `player_data` and `wallet`.

**Debugging walkthrough.** Press *Claim Gift* → the Unity console shows the error with its request
id → paste the rid into Studio's timeline: the call chain (`game_menu` → `wallet`), the log line,
the failure → turn on capture for this player → press again → see the full request → fix the gift
row in Studio → **replay** the captured request → it succeeds.

## 1. Brainrot Exchange — idle meme stock market

**The game.** You run a meme farm that prints coins while you're away and trade shares of absurd
made-up meme companies. Everyone trades in the same market: when a meme crashes, it crashes for the
whole server. Original parody names only (*Skibidi Futures*, *Sigma Grindset Holdings*, *Ohio
Logistics*).

| Extension | Actions | Resources | Events | Cron |
| :--- | :--- | :--- | :--- | :--- |
| `wallet` | `get_balance` (player), `credit`, `debit` (extensions only) | `wallet_balances` | — | — |
| `daily_rewards` | `get_status`, `claim` (player) | `daily_reward_table` (Studio), `daily_claims` | — | — |
| `brainrot_market` | `get_portfolio`, `collect_idle`, `buy_farm`, `buy_shares`, `sell_shares` (player); `storefront_purchase` (webhook) | `meme_stocks` (player-readable, Studio), `portfolios`, `farms` | `prices_ticked` on `market:ticker` | `tick_prices` every minute |

- **Idle income is computed on the server** from the last-collected timestamp, capped at 8 hours, so
  a changed device clock earns nothing.
- **Coins only move through `wallet`**, and every change runs in its one process, so there's no
  double spend and no lock.
- **Studio is the live-ops console:** add a meme stock, change the day-3 reward, and the game picks
  it up without a build.
- **Storefront webhook:** a mock store POSTs a signed "coin pack bought" receipt; the extension
  checks the signature and credits the wallet.

**Demo:** claim the daily bonus → collect idle coins → buy shares → watch the ticker move in two
clients at once → `curl` the storefront webhook → coins arrive → add a stock in Studio → it appears.

## 2. Echo Dungeon — top-down PvE MMO adventure

**The game.** A one-room-at-a-time dungeon crawler: move, swing a sword at slimes, open chests,
walk through doors. It's "an MMO": a feed shows other adventurers' kills and finds as they happen,
and achievement toasts pop up for you.

| Extension | Actions | Resources | Events |
| :--- | :--- | :--- | :--- |
| `adventure_world` | `report_kill`, `report_chest`, `report_zone` (**service**, game server only); `get_progress` (player) | `adventurer_progress` | `enemy_slain`, `chest_opened`, `zone_entered` on `world:feed` |
| `achievements` | `list` (player) | `achievement_defs` (Studio), `unlocks` | `achievement_unlocked` |

- **Trust boundary:** the client never reports its own kills. The **mock game server** — an
  Editor-only component labelled as standing in for the dedicated server — holds a `service`-role
  token, reports the local player's kills, and simulates a few bot adventurers so the feed feels
  populated. Its token is read from the project's `Exoforge/` and never ships in a build.
- **`achievements` has no client-facing writes.** It only listens through `OnEvent`: to
  `adventure_world`, to `brainrot_market` ("first trade"), to `level_share` ("published a level") and
  to `world_boss`. Adding an achievement is a Studio row, not code.

**Demo:** kill three slimes → "Slime Slayer" toast → a bot's kill scrolls by in the feed → go to
Brainrot Exchange, make one trade → back here, "Diamond Hands" is unlocked.

## 3. Snowball Siege — 3D world-boss FPS *(separate Unity project)*

**The game.** First person, a snowball launcher, one giant snowman boss with a health bar shared by
every player online. Hits are resolved by the game server (mocked, same pattern as Echo Dungeon);
everyone sees the bar drop and the hit feed scroll. When it melts, everyone who hit it is paid in
proportion to their damage, and a new boss spawns on a schedule.

| Extension | Actions | Resources | Events | Cron |
| :--- | :--- | :--- | :--- | :--- |
| `world_boss` | `report_hit` (**service**); `get_state` (player) | `boss_state` singleton, `boss_damage` | `boss_damaged`, `boss_defeated` on `world_boss:live` | respawn every 10 min, also on startup |

HP never goes below zero and a boss pays out exactly once, even under concurrent hits. Payouts go
through `wallet.credit`, so the coins show up in Brainrot Exchange. It's a separate project because
this one is set up for 2D rendering only.

## 4. Doodle Dash — draw your own level

**The game.** Paint a level on a 32×18 pixel canvas: black is ground, red is spikes, green is the
start, yellow is the goal. Play it, publish it, then browse other players' levels, play them and
rate them. **The image is the level** — each pixel colour is a tile.

| Extension | Actions | Resources | Events |
| :--- | :--- | :--- | :--- |
| `level_share` | `publish`, `browse`, `rate`, `record_clear` (player) | `levels`, `ratings` | `level_published` |

The PNG goes to the file bucket with metadata (author, width, height). The bucket has no tags, so
the difficulty tag (`difficulty:hard`) is a column on the `levels` row; `level_share` stores the reference, play count and rating. You can only publish a level after
clearing it yourself. The same catalog is reachable over plain HTTP, shown with `curl`.

## 5. Item Shop — shop and inventory

**The game.** A 2D shop window: a two-column grid of item cards, a gold counter, an inventory
panel beside it, and a confirmation popup before each purchase. The UI text is in Portuguese.

| Extension | Actions | Resources |
| :--- | :--- | :--- |
| `items` | `listar_catalogo`, `obter_item` (player); `seed_itens` (studio) | `itens` (player-readable, Studio) |
| `inventario` | `obter_inventario` (player); `add_item`, `add_gold` (extensions only) | `inventario` (gold), `inventario_itens` |
| `loja_itens` | `listar_itens`, `comprar_item` (player); `seed_loja` (studio) | `loja_ofertas` (player-readable, Studio) |

- **The client never sends a price.** `comprar_item` reads the offer and the catalog price on the
  server, then calls `inventario.add_item`, which refuses with `sem_ouro` when the gold is short.
- **Gold lives in `inventario`** and a new player starts with 1000. Only extensions can add gold or items.
- **Studio is the live-ops console:** run `seed_itens` and `seed_loja` once, then edit prices and
  offers as rows; the shop picks them up without a build.

**Demo:** seed → open the shop → buy two potions (stacked) → try the legendary sword twice, the
second is refused → change a price in Studio → it changes in the shop.

---

## What red means

The samples call the SDK the way its docs prescribe, with no workarounds, so they double as a
best-practices test suite: where Exoforge is broken, a sample fails instead of hiding it. Every smoke
script asserts its results and exits non-zero on a wrong one.

On SDK 0.1.48 everything is green: `tools/smoke.sh` (all six samples), `dotnet test
extensions/Exoforge.Extensions.sln` (79 tests) and `tools/unity.sh <project> ArcadeSetup.SetUpAll` for each project.
These failed on 0.1.42 and are the regressions to watch for:

| Would fail | Exoforge bug |
| :--- | :--- |
| Buying shares without the coins or selling shares you don't hold succeeds; the menu gift credits nothing | a failed call to another extension returned null instead of throwing (BUG-1) |
| Storefront webhook | `[ExoWebhook]` handlers got a null `WebhookPayload` (BUG-2) |
| A player triggers the market's price tick | `[ExoCron]` methods were player-callable (SEC-3); now only the scheduler runs them, admin included |
| `achievements.list` hangs | an event during a host call dropped the outer call's reply (BUG-17) |
| Extension tests collide when run in parallel | `ExoTestHost` state was process-wide (CONF-9) |
| The game server's kills land on the server's own account | the transport overwrote a service account's `player_id` (F24) |
| Settings can't load after the first save | SQLite returned boolean columns as strings (menu-11) |
| Error replies carry `{:code, "msg"}` and server stack traces | errors weren't structured and `logs` went to players (SEC-5) |
| Extensions stop building after an SDK upgrade | `nuget.config` kept the old package-cache path (F13) |

Unity's profile has no `IsExternalInit`, so an assembly that declares a `record` carries its own
one-line internal copy - `2d-hub/Assets/Samples/BrainrotExchange/IsExternalInit.cs` and
`2d-doodle-dash/Assets/Samples/DoodleDash/IsExternalInit.cs`, the file **Exoforge ▸ Studio ▸ Settings ▸ C# Records
Support** writes. A copy is per assembly: add one to any asmdef of yours that uses records.

## Layout

```
extensions/            shared server extensions, their tests, Exoforge.Extensions.sln, nuget.config
packages/exoforge.arcade.common/   local UPM package shared by the 2D projects (Samples.Common)
2d-hub/                Unity project: MainMenu + BrainrotExchange
2d-echo-dungeon/       Unity project: EchoDungeon
2d-doodle-dash/        Unity project: DoodleDash
2d-shop/               Unity project: Shop
tools/                 smoke.sh + smoke/*, unity.sh <project>, sync-sdk.sh
```

Each Unity project has its own `Assets/`, `Packages/`, `ProjectSettings/` and `Exoforge/`
(`exoforge.json.example` points `extensions_dir` at `../../extensions`). All projects talk to one
shared cluster; the extensions are built and pushed once.

## Running them

You need only the Exoforge SDK tarball (`com.exoforge.sdk-<version>.tgz`), a running Exoforge
cluster, the .NET SDK 10 (the extensions target `net10.0`) and Unity 6000.6.

1. Place the tarball in every project (git-ignored). Build it in the exoforge checkout with
   `just pack-unity`, then:

   ```bash
   tools/sync-sdk.sh /path/to/exoforge/dist/com.exoforge.sdk-0.1.52.tgz
   ```

   It copies the tarball into each `<project>/Packages/`, rewrites the manifests, and stages the
   extension SDK package in `extensions/.feed/` (git-ignored) for the extensions' `nuget.config`.
2. In the project you want, copy `Exoforge/exoforge.json.example` to `Exoforge/exoforge.json`
   (git-ignored), then open it in Unity. In **Exoforge ▸ Studio**: point the `local` environment at
   your cluster and sign in, **Build & Deploy** the extensions, **Repair & Regenerate Client**.
3. Generate the scene(s), headless or from the **Arcade ▸ Set Up All Scenes** menu:

   ```bash
   tools/unity.sh 2d-hub ArcadeSetup.SetUpAll           # MainMenu, BrainrotExchange
   tools/unity.sh 2d-echo-dungeon ArcadeSetup.SetUpAll  # EchoDungeon
   tools/unity.sh 2d-doodle-dash ArcadeSetup.SetUpAll   # DoodleDash
   tools/unity.sh 2d-shop ArcadeSetup.SetUpAll          # Shop
   ```

   Unity runs are slow: run one project at a time. Then open `Assets/Scenes/<Scene>.unity` and press Play.

Prefer a terminal? `./exo` is installed at each project root by the SDK (use `--dir Exoforge`
from the project root, or run it in `<project>/Exoforge`):

```bash
cd 2d-hub
./exo connect --email <admin email> --password <password> --save --dir Exoforge
for e in wallet daily_rewards brainrot_market adventure_world level_share achievements game_menu items inventario loja_itens; do
  ./exo extension push $e --dir Exoforge
done
./exo sync --dir Exoforge
```

**Service stubs are not committed.** An extension that calls another (`loja_itens` calls `items` and
`inventario`) gets typed stubs in `extensions/<name>/obj/exoforge/PluginServices.g.cs`, made by
`./exo extension stubs <name>` (or `--all`). `./exo extension build` and `push` run that first, providers
before callers, so a fresh clone needs only `./exo extension build --all --dir Exoforge`. Nothing runs
it from MSBuild or when Unity opens a project (an extension that still commits its own `src/Generated/PluginServices.g.cs` keeps it; the pass leaves it alone): a plain `dotnet build` before the first stub pass warns
(EXOFORGE003) and names the command. The Unity client (`Assets/Exoforge/Generated/ExoforgeServices.g.cs`)
is a separate file, regenerated by **Sync Client** or `./exo sync`; if it is missing or old the Studio
window says so in a banner and generates nothing.

**Switching samples on one instance.** Everything in `extensions/` is deployed
except what a project lists under `"excluded_extensions"` in `Exoforge/exoforge.json` (the extensions
its scenes do not need; `./exo extension exclude <name>` / `include <name>` edit the list, and the Unity
Extensions window has an Include toggle per extension). To try another sample on the same instance, wipe
the previous one's extensions in Studio (**Settings > Danger zone > Reset extensions**, or *Delete
everything* to also clear players and config), or from a terminal:

```bash
./exo instance reset --yes --dir Exoforge        # --everything also wipes players, versions, config
```

then, from the project you want to play:

```bash
./exo extension push --all --dir Exoforge        # every extension except the project's excluded ones
```

`tools/smoke.sh` runs every demo script above against the cluster through `./exo call`, using
`2d-hub` as the workspace (`SMOKE_PROJECT=<project>` picks another). `tools/smoke.sh --reset [sample...]` (set `EXO_ADMIN_EMAIL` and `EXO_ADMIN_PASSWORD` so each project signs in again: the server counts one machine as one device, and a login replaces that device's session, so the projects take turns) first runs the reset and
`push --all` above in each sample's own project (`SMOKE_PROJECT` overrides it), so every sample is
checked from a clean instance. With `--reset`, `achievements` runs in `2d-echo-dungeon`, which includes
`adventure_world`, the only source of the events that script sends; events from extensions that project excludes
(for example `trade_made` from `brainrot_market`) do not fire there and are not checked. The game-server token
(`<project>/Exoforge/mock-game-server.token`) is local and never committed.

The storefront webhook in Brainrot Exchange checks receipts against `arcade-demo-secret`. It is a
sample value everyone can read here — a real game keeps its signing secret out of the repository.
