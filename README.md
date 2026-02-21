# CS2 Inventory Simulator Plugin

A CounterStrikeSharp plugin that integrates with [inventory.cstrike.app](https://inventory.cstrike.app) to apply custom skins, agents, gloves, graffiti, and more on your CS2 server.

---

## Configuration

### General

| ConVar | Default | Description |
|---|---|---|
| `invsim_url` | `https://inventory.cstrike.app` | Base URL for the Inventory Simulator API service. |
| `invsim_apikey` | `""` | API key for authenticated requests. Required for StatTrak sync, sign-in, and round-win case drops. Leave empty for read-only mode. |
| `invsim_file` | `inventories.json` | Path to a local JSON file to load inventories from on startup. Useful as a fallback when the API is unavailable. |
| `invsim_chat_prefix` | `- + InventorySimulator + -` | Prefix shown in green before plugin chat messages. Set this to match your server's branding. |

---

### Inventory Refresh (`!ws`)

| ConVar | Default | Description |
|---|---|---|
| `invsim_ws_enabled` | `false` | Allow players to refresh their inventory using the `!ws` command. |
| `invsim_ws_immediately` | `false` | Apply skin changes immediately without requiring a respawn. |
| `invsim_ws_cooldown` | `30` | Cooldown in seconds between inventory refreshes per player. |
| `invsim_ws_url_print_format` | `{Host}` | URL format string shown to players when they use the `!ws` command. |
| `invsim_wslogin` | `false` | Allow players to authenticate via `!wslogin` and receive a login URL. Not recommended for public servers. |

---

### Spray / Graffiti

| ConVar | Default | Description |
|---|---|---|
| `invsim_spray_enabled` | `true` | Enable graffiti spraying via the `!spray` command and/or the use key. |
| `invsim_spray_on_use` | `false` | Trigger a spray when the player presses the use key (E by default). |
| `invsim_spray_cooldown` | `30` | Cooldown in seconds between sprays per player. |
| `invsim_spraychanger_enabled` | `false` | Replace the player's default vanilla spray with their equipped graffiti. |

---

### StatTrak

| ConVar | Default | Description |
|---|---|---|
| `invsim_stattrak_ignore_bots` | `true` | Do not increment StatTrak counters for bot kills. |

---

### Agents / Models

| ConVar | Default | Description |
|---|---|---|
| `invsim_fallback_team` | `false` | Allow using skins from the opposing team if none are equipped for the current team. Current team skins are still prioritised. |
| `invsim_minmodels` | `0` | Controls agent model behaviour: `0` = player's equipped agent, `1` = map default models per team, `2` = force SAS (CT) and Phoenix (T). |

---

### Auto-Reload (Live Skin Sync)

Automatically detects when a player changes their skins on the website and applies them in-game without requiring `!ws`.

The plugin polls each connected player's inventory on a configurable interval. It computes a fingerprint from all equipped item hashes — if the fingerprint changes, the inventory is silently refreshed in the background.

| ConVar | Default | Description |
|---|---|---|
| `invsim_autoreload_enabled` | `false` | Enable automatic inventory refresh when website changes are detected. |
| `invsim_autoreload_interval` | `15` | How often (in seconds) to check each player for changes. Minimum is 5. |

> **Note:** Each poll makes one API request per connected player. Keep `invsim_autoreload_interval` reasonable (15–30s) on servers with many players.

---

### Round Win Case Drops

Gives players on the winning team a weapon case at the end of each round. Requires `invsim_apikey` to be set with `api` or `inventory` scope.

| ConVar | Default | Description |
|---|---|---|
| `invsim_roundwin_enabled` | `false` | Enable round win case drops. |
| `invsim_roundwin_chance` | `0.5` | Probability (0.0–1.0) that a winning player receives a case. Set to `1.0` to guarantee a drop every round. |
| `invsim_roundwin_cases` | `""` | Comma-separated list of weapon case item IDs ordered **oldest to newest**. Newer cases are exponentially more likely to be selected. Leave empty to let the API pick randomly. |
| `invsim_roundwin_weight` | `2.0` | Exponential weight multiplier per step. `1.0` = equal chance, `2.0` = each newer case is 2× more likely, `3.0` = 3× more likely, etc. |

**Recommended `invsim_roundwin_cases` value** (all standard cases, oldest → newest):

```
9129,9131,9130,9132,9133,9135,9136,9137,9142,9143,9144,9154,9163,9173,9175,9205,9236,9237,9394,9395,9396,9406,9407,9408,9409,9419,9420,9421,9422,9425,9424,9432,9433,9434,9437,9442,9501,9503,9506,11422,12453,13314
```

<details>
<summary>Full case list</summary>

| # | Case | ID |
|---|---|---|
| 1 | CS:GO Weapon Case | 9129 |
| 2 | Operation Bravo Case | 9131 |
| 3 | eSports 2013 Case | 9130 |
| 4 | CS:GO Weapon Case 2 | 9132 |
| 5 | eSports 2013 Winter Case | 9133 |
| 6 | Winter Offensive Weapon Case | 9135 |
| 7 | CS:GO Weapon Case 3 | 9136 |
| 8 | Operation Phoenix Weapon Case | 9137 |
| 9 | Huntsman Weapon Case | 9142 |
| 10 | Operation Breakout Weapon Case | 9143 |
| 11 | eSports 2014 Summer Case | 9144 |
| 12 | Operation Vanguard Weapon Case | 9154 |
| 13 | Chroma Case | 9163 |
| 14 | Chroma 2 Case | 9173 |
| 15 | Falchion Case | 9175 |
| 16 | Shadow Case | 9205 |
| 17 | Revolver Case | 9236 |
| 18 | Operation Wildfire Case | 9237 |
| 19 | Chroma 3 Case | 9394 |
| 20 | Gamma Case | 9395 |
| 21 | Gamma 2 Case | 9396 |
| 22 | Glove Case | 9406 |
| 23 | Spectrum Case | 9407 |
| 24 | Operation Hydra Case | 9408 |
| 25 | Spectrum 2 Case | 9409 |
| 26 | Clutch Case | 9419 |
| 27 | Horizon Case | 9420 |
| 28 | Danger Zone Case | 9421 |
| 29 | Prisma Case | 9422 |
| 30 | CS20 Case | 9425 |
| 31 | Shattered Web Case | 9424 |
| 32 | Prisma 2 Case | 9432 |
| 33 | Fracture Case | 9433 |
| 34 | Operation Broken Fang Case | 9434 |
| 35 | Snakebite Case | 9437 |
| 36 | Operation Riptide Case | 9442 |
| 37 | Dreams & Nightmares Case | 9501 |
| 38 | Recoil Case | 9503 |
| 39 | Revolution Case | 9506 |
| 40 | Kilowatt Case | 11422 |
| 41 | Gallery Case | 12453 |
| 42 | Fever Case | 13314 |

</details>

---

## Commands

| Command | Description |
|---|---|
| `css_ws` | Refreshes the player's inventory from the API and displays the configured URL. |
| `css_spray` | Applies the player's equipped graffiti at their current location. |
| `css_wslogin` | Authenticates the player with Inventory Simulator and displays a one-time login URL. |
