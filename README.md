# Exoforge Arcade — samples

Each sample is a tiny game whose design gives one set of Exoforge capabilities a reason to exist.
The server half is plain C# extensions; the client half is one Unity scene. All samples share one
cluster, so they also show extensions reacting to each other: coins from one game show up in
another, and achievements unlock from all of them.

| # | Game | Scene | Extensions | Capabilities it shows |
| :- | :--- | :--- | :--- | :--- |
| 0 | **Main Menu** of a mock game | `MainMenu` | `game_menu` | Hub for every sample · player-readable config · per-player settings · **debugging walkthrough** (rid → timeline → capture → replay) |
| 1 | **Brainrot Exchange** — idle meme stock market | `BrainrotExchange` | `wallet`, `daily_rewards`, `brainrot_market` | Extension → extension calls through generated stubs · server-computed idle income · `[ExoCron]` price ticks broadcast to everyone · Studio-edited live-ops tables · `OnExtensionStart` seeding · `[ExoWebhook]` storefront purchase |
| 2 | **Echo Dungeon** — top-down PvE MMO adventure | `EchoDungeon` | `adventure_world`, `achievements` | A trusted game server reporting through a `service`-scoped action · world events broadcast to all players · `OnEvent` listeners with no client calls · achievements unlocked by events from *every* sample |
| 3 | **Snowball Siege** — 3D world-boss FPS | *separate Unity project* | `world_boss` (pays through `wallet`) | Shared state many players change at once · trusted hit reports · cron respawn · exactly-once payouts across services |
| 4 | **Doodle Dash** — draw-your-level platformer | `DoodleDash` | `level_share` | File bucket uploads with metadata · browse, play and rate player-made content · the same catalog over plain HTTP |

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
  populated. Its token is read from `Exoforge/` and never ships in a build.
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

---

## What red means

The samples call the SDK the way its docs prescribe, with no workarounds, so they double as a
best-practices test suite: where Exoforge is broken, a sample fails instead of hiding it. Every smoke
script asserts its results and exits non-zero on a wrong one.

On SDK 0.1.48 everything is green: `tools/smoke.sh` (all five samples), `dotnet test
Exoforge/extensions/Exoforge.Extensions.sln` (79 tests) and `tools/unity.sh ArcadeSetup.SetUpAll`.
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
one-line internal copy - `Assets/Samples/BrainrotExchange/IsExternalInit.cs` and
`Assets/Samples/DoodleDash/IsExternalInit.cs`, the file **Exoforge ▸ Studio ▸ Settings ▸ C# Records
Support** writes. A copy is per assembly: add one to any asmdef of yours that uses records.

## Running them

You need only the Exoforge SDK tarball (`com.exoforge.sdk-<version>.tgz`), a running Exoforge
cluster, the .NET SDK 10 (the extensions target `net10.0`, which `exo extension new` picks), and Unity 6000.6.

1. Put the tarball in `Packages/` (the manifest refers to it as `file:com.exoforge.sdk-<version>.tgz`).
2. Open the project in Unity. The extensions restore the SDK from `Exoforge/.feed/` (copied from the
   package on the first build; git-ignored) through the committed `Exoforge/nuget.config`. Then, in **Exoforge ▸ Studio**:
   - **Settings:** point the `local` environment at your cluster and sign in.
   - **Extensions:** **Build & Deploy** every extension.
   - Click **Repair & Regenerate Client**.
3. Open `Assets/Scenes/MainMenu.unity` and press Play.

Prefer a terminal? `./exo` is installed at the project root by the SDK:

```bash
./exo connect --email <admin email> --password <password> --save
for e in wallet daily_rewards brainrot_market adventure_world level_share achievements game_menu; do
  ./exo extension push $e
done
./exo sync
```

`tools/smoke.sh` runs every demo script above against the cluster through `./exo call`.

The storefront webhook in Brainrot Exchange checks receipts against `arcade-demo-secret`. It is a
sample value everyone can read here — a real game keeps its signing secret out of the repository.
