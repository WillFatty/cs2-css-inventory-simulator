/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Ian Lucas. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System.Collections.Concurrent;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Timers;
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

    private Timer? _unboxPollTimer;
    private Timer? _unboxBroadcastDrainTimer;

    public void InitializeCustomFeatures()
    {
        ConVars.UnboxPollInterval.ValueChanged += OnUnboxPollIntervalChanged;
        NoCasesPersistence.Load();
        StartUnboxPollTimer();
        StartUnboxBroadcastDrainTimer();
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
    }

    private static string GetRarityChatColor(string? rarity)
    {
        return rarity switch
        {
            "uncommon" => "{lightblue}",
            "rare" => "{blue}",
            "mythical" => "{purple}",
            "legendary" => "{lightpurple}",
            "ancient" => "{red}",
            "immortal" => "{gold}",
            _ => "{white}"
        };
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
            p.PrintToChat(message);
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
        if (!PlayerJoinTimeUtc.TryGetValue(steamId, out var joinTime) || openedAtUtc < joinTime)
            return;
        if (LastBroadcastedCaseOpenedAt.TryGetValue(steamId, out var last) && last == openedAt)
            return;
        LastBroadcastedCaseOpenedAt[steamId] = openedAt;
        var itemName = opening.UnlockedItemName ?? "";
        var rarityColor = GetRarityChatColor(opening.Rarity);
        var message = Localizer[
            "invsim.last_case_unbox_line1",
            opening.UserName,
            itemName,
            rarityColor
        ]
            .ToString();
        PendingUnboxBroadcasts.Enqueue(message.ReplaceColorTags());
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
        if (!PlayerJoinTimeUtc.TryGetValue(steamId, out var joinTime) || completedAtUtc < joinTime)
            return;
        if (
            LastBroadcastedTradeUpCompletedAt.TryGetValue(steamId, out var last)
            && last == completedAt
        )
            return;
        LastBroadcastedTradeUpCompletedAt[steamId] = completedAt;
        var itemName = tradeUp.OutputItemName ?? "";
        var rarityColor = GetRarityChatColor(tradeUp.Rarity);
        var message = Localizer[
            "invsim.last_tradeup_line1",
            tradeUp.UserName,
            itemName,
            rarityColor
        ]
            .ToString();
        PendingUnboxBroadcasts.Enqueue(message.ReplaceColorTags());
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

    private static async Task ApplyAuthoritativeEquippedAsync(
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
            var response = await Api.FetchEquippedAsync(player.SteamID);
            if (response == null)
            {
                // Advance so a bad snapshot tick does not retry forever.
                state.LastEquippedSyncedAt = snapshotSyncedAt;
                return;
            }
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
        var prefix = Rules.GetChatPrefix();

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
                        player.PrintToChat(
                            Runtime.Plugin.Localizer["invsim.roundwin_case", prefix]
                        );
                });
                return;
            }
        }

        await Api.SendAddContainer(userId);
        Server.NextWorldUpdate(() =>
        {
            if (player.IsValid)
                player.PrintToChat(Runtime.Plugin.Localizer["invsim.roundwin_case", prefix]);
        });
    }
}
