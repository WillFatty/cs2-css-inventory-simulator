/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Ian Lucas. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System.Collections.Concurrent;
using System.Text;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Timers;
using HexiumCSUI;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace InventorySimulator;

public partial class InventorySimulator
{
    private static readonly ConcurrentDictionary<ulong, string> LastBroadcastedCaseOpenedAt = new();
    private static readonly ConcurrentDictionary<ulong, string> LastBroadcastedTradeUpCompletedAt =
        new();
    private static readonly ConcurrentDictionary<ulong, DateTime> PlayerJoinTimeUtc = new();
    private static readonly ConcurrentQueue<string> PendingUnboxBroadcasts = new();
    private static readonly Random _roundWinRandom = new();

    /// <summary>
    /// CSS Localizer turns {yellow} into chat bytes; HexiumUI needs {yellow} tags.
    /// </summary>
    private static readonly Dictionary<char, string> ChatCodeToTag = new()
    {
        ['\x01'] = "white",
        ['\x02'] = "darkred",
        ['\x03'] = "lightpurple",
        ['\x04'] = "green",
        ['\x05'] = "olive",
        ['\x06'] = "lime",
        ['\x07'] = "red",
        ['\x08'] = "grey",
        ['\x09'] = "yellow",
        ['\x0A'] = "silver",
        ['\x0B'] = "blue",
        ['\x0C'] = "darkblue",
        ['\x0E'] = "purple",
        ['\x0F'] = "lightred",
        ['\x10'] = "gold",
    };

    /// <summary>SteamIDs that opted out of Panorama notifications (chat fallback).</summary>
    internal static readonly HashSet<ulong> _panoramaOptOut = new();

    private Timer? _unboxPollTimer;
    private Timer? _unboxBroadcastDrainTimer;

    public void InitializeCustomFeatures(bool hotReload)
    {
        ConVars.UnboxPollInterval.ValueChanged += OnUnboxPollIntervalChanged;
        NoCasesPersistence.Load();
        TrailViewPersistence.Load();
        StartUnboxPollTimer();
        StartUnboxBroadcastDrainTimer();

        // GetPlayers() is unsafe during cold Load (globals not ready). Only seed after hot-reload.
        if (hotReload)
            Server.NextWorldUpdate(SeedJoinTimesForConnectedPlayers);
    }

    public void UnloadCustomFeatures()
    {
        _unboxPollTimer?.Kill();
        _unboxPollTimer = null;
        _unboxBroadcastDrainTimer?.Kill();
        _unboxBroadcastDrainTimer = null;
    }

    public void OnUnboxPollIntervalChanged(object? _, float value)
    {
        StartUnboxPollTimer();
    }

    private void StartUnboxPollTimer()
    {
        _unboxPollTimer?.Kill();
        var interval = Math.Max(1f, ConVars.UnboxPollInterval.Value);
        _unboxPollTimer = AddTimer(
            interval,
            () => HandleLastCaseOpeningAndTradeUpPoll(),
            TimerFlags.REPEAT
        );
    }

    private void StartUnboxBroadcastDrainTimer()
    {
        _unboxBroadcastDrainTimer?.Kill();
        _unboxBroadcastDrainTimer = AddTimer(
            0.1f,
            () => DrainOneUnboxBroadcast(),
            TimerFlags.REPEAT
        );
    }

    private static void SeedJoinTimesForConnectedPlayers()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.IsBot || player.SteamID == 0)
                continue;
            PlayerJoinTimeUtc.TryAdd(player.SteamID, DateTime.UtcNow);
        }
    }

    public void RecordPlayerJoinTime(CCSPlayerController player)
    {
        if (player.IsValid && !player.IsBot)
            PlayerJoinTimeUtc[player.SteamID] = DateTime.UtcNow;
    }

    public static void CleanupCustomPlayerState(ulong steamId)
    {
        LastBroadcastedCaseOpenedAt.TryRemove(steamId, out _);
        LastBroadcastedTradeUpCompletedAt.TryRemove(steamId, out _);
        PlayerJoinTimeUtc.TryRemove(steamId, out _);
        _panoramaOptOut.Remove(steamId);
    }

    private static string GetRarityChatColor(string? rarity)
    {
        return rarity?.Trim().ToLowerInvariant() switch
        {
            "common" or "default" => "{bluegrey}",
            "uncommon" => "{lightblue}",
            "rare" => "{blue}",
            "mythical" => "{purple}",
            "legendary" => "{lightpurple}",
            "ancient" => "{red}",
            "immortal" => "{gold}",
            _ => "{white}"
        };
    }

    /// <summary>
    /// Convert Localizer chat color bytes back to {color} tags for HexiumUI.
    /// Leaves existing {color} tags (e.g. rarity arg) intact.
    /// </summary>
    private static string ToPanoramaColorTags(string message)
    {
        var needsConvert = false;
        foreach (var ch in message)
        {
            if (ch is >= '\u0001' and <= '\u0010')
            {
                needsConvert = true;
                break;
            }
        }

        if (!needsConvert)
            return message.TrimStart();

        var sb = new StringBuilder(message.Length + 24);
        foreach (var ch in message)
        {
            if (ch is >= '\u0001' and <= '\u0010')
            {
                if (ChatCodeToTag.TryGetValue(ch, out var name))
                {
                    sb.Append('{');
                    sb.Append(name);
                    sb.Append('}');
                }

                continue;
            }

            sb.Append(ch);
        }

        return sb.ToString().TrimStart();
    }

    public void DrainOneUnboxBroadcast()
    {
        if (!PendingUnboxBroadcasts.TryDequeue(out var message))
            return;

        foreach (
            var p in Utilities
                .GetPlayers()
                .Where(p => p.IsValid && !p.IsBot && !p.GetState().HideUnboxTradeUpMessages)
        )
        {
            // Broadcast lines are fully formatted; chat should not add the server prefix again.
            NotifyPlayer(p, "Inventory", message, duration: 5, chatPrefix: false);
        }
    }

    private static string StripLeadingBulletPrefix(string message)
    {
        var trimmed = message.TrimStart();
        if (trimmed.StartsWith("••", StringComparison.Ordinal))
            return trimmed[2..].TrimStart();
        if (trimmed.StartsWith("•", StringComparison.Ordinal))
            return trimmed[1..].TrimStart();
        return message;
    }

    internal static bool WantsPanorama(CCSPlayerController player)
    {
        if (!ConVars.UsePanoramaUI.Value)
            return false;

        var steamId = player.AuthorizedSteamID?.SteamId64;
        return steamId is null || !_panoramaOptOut.Contains(steamId.Value);
    }

    /// <summary>
    /// Routes a player notification to Panorama when enabled for them, otherwise chat.
    /// <paramref name="message"/> may use {color} tags or Localizer chat color bytes.
    /// When <paramref name="chatPrefix"/> is true, the configured chat prefix is prepended for chat only.
    /// </summary>
    internal static void NotifyPlayer(
        CCSPlayerController? player,
        string title,
        string message,
        int duration = 5,
        bool chatPrefix = true
    )
    {
        if (player is null || !player.IsValid || player.IsBot || player.IsHLTV)
            return;

        var normalized = ToPanoramaColorTags(message);

        if (WantsPanorama(player))
        {
            HexiumUI.ShowNotification(
                player,
                title,
                StripLeadingBulletPrefix(normalized),
                duration
            );
            return;
        }

        var chatBody = normalized.ReplaceColorTags();
        player.PrintToChat(chatPrefix ? $"{Rules.GetChatPrefix()}{chatBody.TrimStart()}" : chatBody);
    }

    public void HandleLastCaseOpeningAndTradeUpPoll()
    {
        var players = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot).ToList();
        if (players.Count == 0)
            return;

        _ = ProcessPluginSnapshotAsync(players);
    }

    private async Task ProcessPluginSnapshotAsync(List<CCSPlayerController> toPoll)
    {
        var steamIds = toPoll.Select(p => p.SteamID.ToString()).ToList();
        var snapshot = await Api.FetchPluginSnapshot(steamIds);
        if (snapshot == null)
            return;

        // After await we are off the main thread — only schedule game-thread work.
        Server.NextWorldUpdate(() =>
        {
            foreach (var player in toPoll)
            {
                if (!player.IsValid)
                    continue;
                var steamIdStr = player.SteamID.ToString();
                if (!snapshot.TryGetValue(steamIdStr, out var entry) || entry == null)
                    continue;

                ProcessCaseOpeningFromSnapshot(player, entry);
                ProcessTradeUpFromSnapshot(player, entry);
                ProcessEquippedFromSnapshot(player, entry);
            }
        });
    }

    private void ProcessCaseOpeningFromSnapshot(
        CCSPlayerController player,
        PluginSnapshotUserEntry entry
    )
    {
        var opening = entry.LastCaseOpening;
        if (opening == null || string.IsNullOrEmpty(opening.OpenedAt))
            return;

        var steamId = player.SteamID;
        var openedAt = opening.OpenedAt;
        if (
            !DateTime.TryParse(
                openedAt,
                null,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var openedAtUtc
            )
        )
            return;

        // Missing join time (e.g. race after reload): seed now and skip historical openings.
        if (!PlayerJoinTimeUtc.TryGetValue(steamId, out var joinTime))
        {
            PlayerJoinTimeUtc[steamId] = DateTime.UtcNow;
            return;
        }

        if (openedAtUtc < joinTime)
            return;
        if (LastBroadcastedCaseOpenedAt.TryGetValue(steamId, out var last) && last == openedAt)
            return;

        LastBroadcastedCaseOpenedAt[steamId] = openedAt;
        var itemName = opening.UnlockedItemName ?? "";
        var rarityColor = GetRarityChatColor(opening.Rarity);
        // Localizer converts {yellow}/{white} to chat bytes — restore tags for Panorama.
        var message = ToPanoramaColorTags(
            Localizer[
                "invsim.last_case_unbox_line1",
                opening.UserName,
                itemName,
                rarityColor
            ]
                .ToString()
        );
        PendingUnboxBroadcasts.Enqueue(message);
    }

    private void ProcessTradeUpFromSnapshot(
        CCSPlayerController player,
        PluginSnapshotUserEntry entry
    )
    {
        var tradeUp = entry.LastTradeUp;
        if (tradeUp == null || string.IsNullOrEmpty(tradeUp.CompletedAt))
            return;

        var steamId = player.SteamID;
        var completedAt = tradeUp.CompletedAt;
        if (
            !DateTime.TryParse(
                completedAt,
                null,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var completedAtUtc
            )
        )
            return;

        if (!PlayerJoinTimeUtc.TryGetValue(steamId, out var joinTime))
        {
            PlayerJoinTimeUtc[steamId] = DateTime.UtcNow;
            return;
        }

        if (completedAtUtc < joinTime)
            return;
        if (
            LastBroadcastedTradeUpCompletedAt.TryGetValue(steamId, out var last)
            && last == completedAt
        )
            return;

        LastBroadcastedTradeUpCompletedAt[steamId] = completedAt;
        var itemName = tradeUp.OutputItemName ?? "";
        var rarityColor = GetRarityChatColor(tradeUp.Rarity);
        var message = ToPanoramaColorTags(
            Localizer[
                "invsim.last_tradeup_line1",
                tradeUp.UserName,
                itemName,
                rarityColor
            ]
                .ToString()
        );
        PendingUnboxBroadcasts.Enqueue(message);
    }

    private void ProcessEquippedFromSnapshot(
        CCSPlayerController player,
        PluginSnapshotUserEntry entry
    )
    {
        var state = player.GetState();
        if (state.IsFetching || state.IsAutoReloading || state.IsLoadedFromFile)
            return;

        // Do not trust snapshot `equipped` — it can still hold individually-selected
        // skins after a loadout is equipped, which caused a flash then revert. Use
        // syncedAt as the dirty signal and always refresh from /api/equipped/v5.
        if (entry.SyncedAt == 0)
            return;
        if (entry.SyncedAt == state.LastEquippedSyncedAt)
            return;

        if (state.InventoryFingerprint == null && state.Inventory != null)
        {
            state.InventoryFingerprint = state.Inventory.ComputeFingerprint();
            state.LastEquippedSyncedAt = entry.SyncedAt;
            return;
        }

        _ = ApplyAuthoritativeEquippedAsync(player, entry.SyncedAt);
    }

    private async Task ApplyAuthoritativeEquippedAsync(
        CCSPlayerController player,
        long snapshotSyncedAt
    )
    {
        var state = player.GetState();
        if (state.IsFetching || state.IsAutoReloading || state.IsLoadedFromFile)
            return;
        state.IsAutoReloading = true;
        var applyScheduled = false;
        try
        {
            var steamId = player.SteamID;
            var response = await Api.FetchEquippedAsync(steamId);
            if (response == null)
            {
                // Advance so a bad snapshot tick does not retry forever.
                state.LastEquippedSyncedAt = snapshotSyncedAt;
                return;
            }
            ApplyRankTrailsFromEquipped(steamId, response.Trails);
            var newInventory = new PlayerInventory(response);
            var newFingerprint = newInventory.ComputeFingerprint();
            // StatTrak-only sync (kills): keep counts up to date without re-giving skins.
            if (state.InventoryFingerprint == newFingerprint)
            {
                newInventory.InitializeWearOverrides();
                state.Inventory = newInventory;
                state.LastEquippedSyncedAt = snapshotSyncedAt;
                return;
            }
            newInventory.InitializeWearOverrides();
            applyScheduled = true;
            Server.NextWorldUpdate(() =>
            {
                try
                {
                    if (!player.IsValid)
                        return;
                    var current = player.GetState();
                    if (current.IsLoadedFromFile)
                        return;
                    var oldInventory = current.Inventory;
                    current.ClearEconItemView();
                    current.WsUpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    current.Inventory = newInventory;
                    current.InventoryFingerprint = newFingerprint;
                    current.LastEquippedSyncedAt = snapshotSyncedAt;
                    player.HandleInventoryLoad();
                    player.RegiveAgent(newInventory, oldInventory);
                    player.RegiveGloves(newInventory, oldInventory);
                    player.RegiveWeapons(newInventory, oldInventory);
                    player.RegivePet(newInventory, oldInventory);
                }
                finally
                {
                    if (player.IsValid)
                        player.GetState().IsAutoReloading = false;
                }
            });
        }
        catch
        {
            state.LastEquippedSyncedAt = snapshotSyncedAt;
        }
        finally
        {
            if (!applyScheduled)
                state.IsAutoReloading = false;
        }
    }

    public static int PickWeightedCaseId(int[] caseIds, float weightFactor)
    {
        var count = caseIds.Length;
        var weights = new double[count];
        double total = 0;

        for (var i = 0; i < count; i++)
        {
            weights[i] = Math.Pow(weightFactor, i);
            total += weights[i];
        }

        var roll = _roundWinRandom.NextDouble() * total;
        double cumulative = 0;

        for (var i = 0; i < count; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative)
                return caseIds[i];
        }

        return caseIds[count - 1];
    }

    public static async void HandleRoundWinCaseReward(CCSPlayerController player)
    {
        if (!Api.HasApiKey())
            return;

        var chance = ConVars.RoundWinChance.Value;
        if (chance <= 0f)
            return;
        if (chance < 1f && _roundWinRandom.NextDouble() >= chance)
            return;

        var casesRaw = ConVars.RoundWinCases.Value.Trim();
        var userId = player.SteamID.ToString();

        if (!string.IsNullOrEmpty(casesRaw))
        {
            var parsedIds = casesRaw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var id) ? id : -1)
                .Where(id => id > 0)
                .ToArray();

            if (parsedIds.Length > 0)
            {
                var chosenId = PickWeightedCaseId(parsedIds, ConVars.RoundWinWeight.Value);
                await Api.SendAddItem(userId, chosenId);
                Server.NextWorldUpdate(() =>
                {
                    if (player.IsValid)
                        NotifyRoundWinCase(player);
                });
                return;
            }
        }

        await Api.SendAddContainer(userId);
        Server.NextWorldUpdate(() =>
        {
            if (player.IsValid)
                NotifyRoundWinCase(player);
        });
    }

    private static void NotifyRoundWinCase(CCSPlayerController player)
    {
        NotifyPlayer(
            player,
            "Inventory",
            Runtime.Plugin.Localizer["invsim.roundwin_case", ""].ToString()
        );
    }
}
