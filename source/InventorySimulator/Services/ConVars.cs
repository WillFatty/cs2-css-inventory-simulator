/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Ian Lucas. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;

namespace InventorySimulator;

public static class ConVars
{
    public static readonly FakeConVar<string> Url = new(
        "invsim_url",
        "API URL for the Inventory Simulator service.",
        "https://inventory.cstrike.app"
    );

    public static readonly FakeConVar<string> ApiKey = new(
        "invsim_apikey",
        "API key for the Inventory Simulator service.",
        ""
    );

    public static readonly FakeConVar<string> File = new(
        "invsim_file",
        "Inventory data file to load when the plugin starts.",
        "inventories.json"
    );

    public static readonly FakeConVar<bool> IsWsEnabled = new(
        "invsim_ws_enabled",
        "Allow players to refresh their inventory using the !ws command.",
        false
    );

    public static readonly FakeConVar<bool> IsWsImmediately = new(
        "invsim_ws_immediately",
        "Apply skin changes immediately without requiring a respawn.",
        false
    );

    public static readonly FakeConVar<int> WsCooldown = new(
        "invsim_ws_cooldown",
        "Cooldown duration in seconds between inventory refreshes per player.",
        30
    );

    public static readonly FakeConVar<string> ChatPrefix = new(
        "invsim_chat_prefix",
        "Prefix displayed before chat messages.",
        "{grey}- {gold}+ HexiumCS2 {gold}+ {grey}-"
    );

    public static readonly FakeConVar<string> WsUrlPrintFormat = new(
        "invsim_ws_url_print_format",
        "URL format string displayed when using the !ws command.",
        "{Host}"
    );

    public static readonly FakeConVar<bool> IsWsLogin = new(
        "invsim_wslogin",
        "Allow players to authenticate with Inventory Simulator and display their login URL (not recommended).",
        false
    );

    public static readonly FakeConVar<bool> IsPersistInventory = new(
        "invsim_persist_inventory",
        "Keep a player's cached inventory after they disconnect.",
        false
    );

    public static readonly FakeConVar<bool> IsRequireInventory = new(
        "invsim_require_inventory",
        "Require the player's inventory to be fetched before allowing them to join the game.",
        false
    );

    public static readonly FakeConVar<bool> IsSprayEnabled = new(
        "invsim_spray_enabled",
        "Enable spraying via the !spray command and/or use key.",
        true
    );

    public static readonly FakeConVar<bool> IsSprayOnUse = new(
        "invsim_spray_on_use",
        "Apply spray when the player taps the use key.",
        false
    );

    public static readonly FakeConVar<int> SprayCooldown = new(
        "invsim_spray_cooldown",
        "Cooldown duration in seconds between sprays per player.",
        30
    );

    public static readonly FakeConVar<bool> IsSprayChangerEnabled = new(
        "invsim_spraychanger_enabled",
        "Replace the player's vanilla spray with their equipped graffiti.",
        false
    );

    public static readonly FakeConVar<bool> IsPublicApiStatTrakIncrement = new(
        "invsim_public_api_stattrak_increment",
        "Send keyless StatTrak increment requests to the public API when invsim_apikey is not set.",
        true
    );

    public static readonly FakeConVar<bool> IsPublicApiSprayConsume = new(
        "invsim_public_api_spray_consume",
        "Send keyless graffiti consume requests to the public API when invsim_apikey is not set.",
        true
    );

    public static readonly FakeConVar<bool> IsStatTrakIgnoreBots = new(
        "invsim_stattrak_ignore_bots",
        "Ignore StatTrak kill count increments for bot kills.",
        true
    );

    public static readonly FakeConVar<bool> IsRoundWinEnabled = new(
        "invsim_roundwin_enabled",
        "Give winning team players a weapon case at the end of each round.",
        false
    );

    public static readonly FakeConVar<float> RoundWinChance = new(
        "invsim_roundwin_chance",
        "Probability (0.0–1.0) that a winning player receives a case each round.",
        0.5f
    );

    public static readonly FakeConVar<string> RoundWinCases = new(
        "invsim_roundwin_cases",
        "Comma-separated list of weapon case item IDs ordered from oldest to newest. Newer cases are exponentially more likely to be chosen. Leave empty to let the API pick randomly.",
        ""
    );

    public static readonly FakeConVar<float> RoundWinWeight = new(
        "invsim_roundwin_weight",
        "Exponential weight multiplier applied per position in invsim_roundwin_cases (e.g. 2.0 means each newer case is twice as likely as the previous one).",
        2.0f
    );

    public static readonly FakeConVar<float> UnboxPollInterval = new(
        "invsim_unbox_poll_interval",
        "Interval in seconds between polling for case openings and trade-ups. Higher = fewer API calls, slower to show new unboxes. Minimum 1.",
        1f
    );

    public static readonly FakeConVar<bool> UsePanoramaUI = new(
        "invsim_panorama_ui",
        "When true, case opening and trade-up notifications use the HexiumCS-UI Workshop Panorama toast instead of chat.",
        false
    );

    public static readonly FakeConVar<bool> IsFallbackTeam = new(
        "invsim_fallback_team",
        "Allow using skins from any team (prioritizes current team first).",
        false
    );

    public static readonly FakeConVar<bool> IsLoadoutEnabled = new(
        "invsim_loadout_enabled",
        "Give players the weapons of their equipped loadout instead of their game loadout.",
        true
    );

    public static readonly FakeConVar<int> MinModels = new(
        "invsim_minmodels",
        "Enable player agents (0 = enabled, 1 = use map models per team, 2 = SAS & Phoenix).",
        0
    );

    public static readonly FakeConVar<bool> IsPetEnabled = new(
        "invsim_pet_enabled",
        "Allow players' pets to spawn.",
        true
    );

    public static readonly FakeConVar<bool> IsPetImmortal = new(
        "invsim_pet_immortal",
        "Prevent players' pets from taking damage.",
        false
    );

    public static readonly FakeConVar<bool> IsPetFreeRoam = new(
        "invsim_pet_free_roam",
        "Allow players' pets to keep roaming after freeze time ends.",
        false
    );

    public static readonly FakeConVar<bool> IsPetRespawn = new(
        "invsim_pet_respawn",
        "Give players a new pet when they spawn without a living one.",
        false
    );

    public static readonly FakeConVar<bool> IsPetRespawnWarmupOnly = new(
        "invsim_pet_respawn_warmup_only",
        "Only respawn pets during warmup.",
        true
    );

    public static readonly FakeConVar<bool> IsRankEnabled = new(
        "invsim_rank_enabled",
        "Enable kill/round-win points and the leaderboard (requires invsim_apikey).",
        true
    );

    public static readonly FakeConVar<int> RankStartingPoints = new(
        "invsim_rank_starting_points",
        "Points a player starts with the first time their rank record is created.",
        1000
    );

    public static readonly FakeConVar<int> RankKillPoints = new(
        "invsim_rank_kill_points",
        "Points awarded per kill.",
        5
    );

    public static readonly FakeConVar<int> RankHeadshotPoints = new(
        "invsim_rank_headshot_points",
        "Bonus points for a headshot kill.",
        2
    );

    public static readonly FakeConVar<int> RankKnifeKillPoints = new(
        "invsim_rank_knife_kill_points",
        "Bonus points for a knife kill.",
        5
    );

    public static readonly FakeConVar<int> RankZeusKillPoints = new(
        "invsim_rank_zeus_kill_points",
        "Bonus points for a zeus kill.",
        3
    );

    public static readonly FakeConVar<int> RankNoscopePoints = new(
        "invsim_rank_noscope_points",
        "Bonus points for a noscope kill.",
        3
    );

    public static readonly FakeConVar<int> RankSmokeKillPoints = new(
        "invsim_rank_smoke_kill_points",
        "Bonus points for a kill through smoke.",
        2
    );

    public static readonly FakeConVar<int> RankWallbangPoints = new(
        "invsim_rank_wallbang_points",
        "Bonus points for a wallbang kill.",
        2
    );

    public static readonly FakeConVar<int> RankDeathPoints = new(
        "invsim_rank_death_points",
        "Points awarded on death (negative values deduct points).",
        -2
    );

    public static readonly FakeConVar<int> RankRoundWinPoints = new(
        "invsim_rank_roundwin_points",
        "Points awarded to each player on the winning team at round end.",
        2
    );

    public static readonly FakeConVar<int> RankRoundLossPoints = new(
        "invsim_rank_roundloss_points",
        "Points awarded to each player on the losing team at round end (negative values deduct points).",
        -1
    );

    public static readonly FakeConVar<int> RankMinPlayers = new(
        "invsim_rank_min_players",
        "Minimum number of human players required for points to be awarded.",
        2
    );

    public static readonly FakeConVar<bool> RankPointsForBots = new(
        "invsim_rank_points_for_bots",
        "Allow points to be earned from kills on bots.",
        false
    );

    public static readonly FakeConVar<bool> RankPointsDuringWarmup = new(
        "invsim_rank_points_during_warmup",
        "Allow points to be earned during warmup.",
        false
    );

    public static readonly FakeConVar<bool> RankShowPointMessages = new(
        "invsim_rank_show_messages",
        "Show chat/panorama notifications when points change.",
        true
    );

    public static readonly FakeConVar<bool> RankShowPremierOnScoreboard = new(
        "invsim_rank_show_premier",
        "Display points as a Premier ranking on the scoreboard.",
        true
    );


    public static void Initialize(BasePlugin plugin)
    {
        plugin.RegisterFakeConVars(typeof(ConVars));
    }
}
