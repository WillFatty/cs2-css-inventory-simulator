/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Ian Lucas. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System.Collections.Concurrent;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;

namespace InventorySimulator;

public partial class InventorySimulator
{
    private static readonly ConcurrentDictionary<ulong, string> LastBroadcastedCaseOpenedAt = new();
    private static readonly ConcurrentDictionary<ulong, string> LastBroadcastedTradeUpCompletedAt =
        new();
    private static readonly ConcurrentDictionary<ulong, DateTime> PlayerJoinTimeUtc = new();
    private static readonly ConcurrentQueue<string> PendingUnboxBroadcasts = new();
    private int _unboxPollRoundRobinIndex;

    // ========================================================================
    // Connection & Initialization
    // ========================================================================

    public void HandlePlayerConnect(CCSPlayerController player)
    {
        player.Revalidate();
        HandlePlayerInventoryRefresh(player);
    }

    public void RecordPlayerJoinTime(CCSPlayerController player)
    {
        if (player.IsValid && !player.IsBot)
            PlayerJoinTimeUtc[player.SteamID] = DateTime.UtcNow;
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
        foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot))
            p.PrintToChat(message);
    }

    public void HandleLastCaseOpeningAndTradeUpPoll()
    {
        var players = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot).ToList();
        if (players.Count == 0)
            return;
        var maxPerTick = Math.Max(1, ConVars.UnboxPollMaxPlayersPerTick.Value);
        var start = _unboxPollRoundRobinIndex % players.Count;
        _unboxPollRoundRobinIndex = (start + maxPerTick) % players.Count;
        var toPoll = new List<CCSPlayerController>();
        for (var i = 0; i < maxPerTick && i < players.Count; i++)
        {
            var idx = (start + i) % players.Count;
            toPoll.Add(players[idx]);
        }
        foreach (var player in toPoll)
        {
            _ = PollPlayerLastCaseOpeningAsync(player);
            _ = PollPlayerLastTradeUpAsync(player);
        }
    }

    private async Task PollPlayerLastCaseOpeningAsync(CCSPlayerController player)
    {
        var steamId = player.SteamID;
        var opening = await Api.FetchLastCaseOpening(steamId.ToString());
        if (opening == null || string.IsNullOrEmpty(opening.OpenedAt))
            return;
        var openedAt = opening.OpenedAt;
        if (!DateTime.TryParse(openedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var openedAtUtc))
            return;
        if (!PlayerJoinTimeUtc.TryGetValue(steamId, out var joinTime) || openedAtUtc < joinTime)
            return;
        Server.NextWorldUpdate(() =>
        {
            if (!player.IsValid)
                return;
            if (LastBroadcastedCaseOpenedAt.TryGetValue(steamId, out var last) && last == openedAt)
                return;
            LastBroadcastedCaseOpenedAt[steamId] = openedAt;
            var itemName = opening.UnlockedItemName ?? "";
            var rarityColor = GetRarityChatColor(opening.Rarity);
            var message = Localizer["invsim.last_case_unbox_line1", opening.UserName, itemName, rarityColor].ToString();
            PendingUnboxBroadcasts.Enqueue(message.ReplaceColorTags());
        });
    }

    private async Task PollPlayerLastTradeUpAsync(CCSPlayerController player)
    {
        var steamId = player.SteamID;
        var tradeUp = await Api.FetchLastTradeUp(steamId.ToString());
        if (tradeUp == null || string.IsNullOrEmpty(tradeUp.CompletedAt))
            return;
        var completedAt = tradeUp.CompletedAt;
        if (!DateTime.TryParse(completedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var completedAtUtc))
            return;
        if (!PlayerJoinTimeUtc.TryGetValue(steamId, out var joinTime) || completedAtUtc < joinTime)
            return;
        Server.NextWorldUpdate(() =>
        {
            if (!player.IsValid)
                return;
            if (LastBroadcastedTradeUpCompletedAt.TryGetValue(steamId, out var last) && last == completedAt)
                return;
            LastBroadcastedTradeUpCompletedAt[steamId] = completedAt;
            var itemName = tradeUp.OutputItemName ?? "";
            var rarityColor = GetRarityChatColor(tradeUp.Rarity);
            var message = Localizer["invsim.last_tradeup_line1", tradeUp.UserName, itemName, rarityColor].ToString();
            PendingUnboxBroadcasts.Enqueue(message.ReplaceColorTags());
        });
    }

    // ========================================================================
    // Inventory Fetch & Load Operations
    // ========================================================================

    public static async Task HandlePlayerInventoryFetch(
        CCSPlayerController player,
        bool force = false
    )
    {
        var controllerState = player.GetState();
        var existing = controllerState.Inventory;
        if (!force && controllerState.Inventory != null)
            return;
        if (controllerState.IsFetching)
            return;
        controllerState.IsFetching = true;
        var response = await Api.FetchEquipped(player.SteamID);
        if (response != null)
        {
            var inventory = new PlayerInventory(response);
            if (existing != null)
                inventory.WeaponWearCache = existing.WeaponWearCache;
            inventory.InitializeWearOverrides();
            controllerState.WsUpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            controllerState.Inventory = inventory;
            controllerState.InventoryFingerprint = inventory.ComputeFingerprint();
        }
        controllerState.IsFetching = false;
        controllerState.TriggerPostFetch();
    }

    public async void HandlePlayerInventoryRefresh(CCSPlayerController player, bool force = false)
    {
        if (!force)
        {
            await HandlePlayerInventoryFetch(player);
            Server.NextWorldUpdate(() =>
            {
                if (player.IsValid)
                    HandlePlayerInventoryLoad(player);
            });
            return;
        }
        var oldInventory = player.GetState().Inventory;
        await HandlePlayerInventoryFetch(player, true);
        Server.NextWorldUpdate(() =>
        {
            if (player.IsValid)
            {
                player.PrintToChat(Localizer["invsim.ws_completed"]);
                HandlePlayerInventoryLoad(player);
                HandlePostPlayerInventoryRefresh(player, oldInventory);
            }
        });
    }

    public static void HandlePlayerInventoryLoad(CCSPlayerController player)
    {
        var inventory = player.InventoryServices?.GetInventory();
        if (inventory?.IsValid == true)
            inventory.SendInventoryUpdateEvent();
    }

    public static void HandlePostPlayerInventoryRefresh(
        CCSPlayerController player,
        PlayerInventory? oldInventory
    )
    {
        var inventory = player.GetState().Inventory;
        if (inventory != null && ConVars.IsWsImmediately.Value)
        {
            player.RegiveAgent(inventory, oldInventory);
            player.RegiveGloves(inventory, oldInventory);
            player.RegiveWeapons(inventory, oldInventory);
        }
    }

    // ========================================================================
    // Runtime: StatTrak Operations
    // ========================================================================

    public void HandlePlayerWeaponStatTrakIncrement(
        CCSPlayerController player,
        string designerName,
        string weaponItemId
    )
    {
        var weapon = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;
        if (
            weapon == null
            || !weapon.HasCustomItemID()
            || weapon.AttributeManager.Item.AccountID
                != new CSteamID(player.SteamID).GetAccountID().m_AccountID
            || weapon.AttributeManager.Item.ItemID != ulong.Parse(weaponItemId)
        )
            return;
        var inventory = player.GetState().Inventory;
        var isFallbackTeam = ConVars.IsFallbackTeam.Value;
        var item = ItemHelper.IsMeleeDesignerName(designerName)
            ? inventory?.GetKnife(player.TeamNum, isFallbackTeam)
            : inventory?.GetWeapon(
                player.TeamNum,
                weapon.AttributeManager.Item.ItemDefinitionIndex,
                isFallbackTeam
            );
        if (item == null || item.Uid == null || item.Stattrak == null || item.Stattrak.Value < 0)
            return;
        item.Stattrak += 1;
        var statTrak = TypeHelper.ViewAs<int, float>(item.Stattrak.Value);
        weapon.AttributeManager.Item.NetworkedDynamicAttributes.SetOrAddAttributeValueByName(
            "kill eater",
            statTrak
        );
        HandleStatTrakIncrement(player.SteamID, item.Uid.Value);
    }

    public static void HandlePlayerMusicKitStatTrakIncrement(
        EventRoundMvp @event,
        CCSPlayerController player
    )
    {
        var item = player.GetState().Inventory?.MusicKit;
        if (item != null && item.Uid != null && item.Stattrak != null && item.Stattrak >= 0)
        {
            item.Stattrak += 1;
            @event.Musickitmvps = item.Stattrak.Value;
            HandleStatTrakIncrement(player.SteamID, item.Uid.Value);
        }
    }

    public static async void HandleStatTrakIncrement(ulong userId, int targetUid)
    {
        if (Api.HasApiKey())
            await Api.SendStatTrakIncrement(targetUid, userId.ToString());
    }

    // ========================================================================
    // Runtime: Graffiti/Spray Operations
    // ========================================================================

    public void HandleClientProcessUsercmds(CCSPlayerController player)
    {
        if (
            (player.Buttons & PlayerButtons.Use) != 0
            && player.PlayerPawn.Value?.IsAbleToApplySpray() == true
        )
        {
            var controllerState = player.GetState();
            if (player.IsUseCmdBusy())
                controllerState.IsUseCmdBlocked = true;
            controllerState.DisposeUseCmdTimer();
            controllerState.UseCmdTimer = AddTimer(
                0.1f,
                () =>
                {
                    if (controllerState.IsUseCmdBlocked)
                        controllerState.IsUseCmdBlocked = false;
                    else if (player.IsValid && !player.IsUseCmdBusy())
                        player.ExecuteClientCommandFromServer("css_spray");
                }
            );
        }
    }

    public unsafe void HandlePlayerGraffitiSpray(CCSPlayerController player)
    {
        if (!player.IsValid)
            return;
        var item = player.GetState().Inventory?.Graffiti;
        if (item == null || item.Def == null || item.Tint == null)
            return;
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || pawn.LifeState != (int)LifeState_t.LIFE_ALIVE)
            return;
        var movementServices = pawn.MovementServices?.As<CCSPlayer_MovementServices>();
        if (movementServices == null)
            return;
        var trace = stackalloc CGameTrace[1];
        if (!pawn.IsAbleToApplySpray((nint)trace) || (nint)trace == nint.Zero)
            return;
        player.EmitSound("SprayCan.Shake");
        player.GetState().SprayUsedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var endPos = SchemaHelper.ToVector(trace->EndPos);
        var hitNormal = SchemaHelper.ToVector(trace->HitNormal);
        var sprayDecal = Utilities.CreateEntityByName<CPlayerSprayDecal>("player_spray_decal");
        if (sprayDecal != null)
        {
            sprayDecal.EndPos.Add(endPos);
            sprayDecal.Start.Add(endPos);
            sprayDecal.Left.Add(movementServices.Left);
            sprayDecal.Normal.Add(hitNormal);
            sprayDecal.AccountID = (uint)player.SteamID;
            sprayDecal.Player = item.Def.Value;
            sprayDecal.TintID = item.Tint.Value;
            sprayDecal.DispatchSpawn();
            player.EmitSound("SprayCan.Paint");
        }
    }

    public static void HandlePlayerSprayDecalCreated(
        CCSPlayerController player,
        CPlayerSprayDecal sprayDecal
    )
    {
        var item = player.GetState().Inventory?.Graffiti;
        if (item != null && item.Def != null && item.Tint != null)
        {
            sprayDecal.Player = item.Def.Value;
            Utilities.SetStateChanged(sprayDecal, "CPlayerSprayDecal", "m_nPlayer");
            sprayDecal.TintID = item.Tint.Value;
            Utilities.SetStateChanged(sprayDecal, "CPlayerSprayDecal", "m_nTintID");
        }
    }

    // ========================================================================
    // Runtime: Authentication
    // ========================================================================

    public async void HandleSignIn(CCSPlayerController player)
    {
        var controllerState = player.GetState();
        if (controllerState.IsFetching)
            return;
        controllerState.IsFetching = true;
        var response = await Api.SendSignIn(player.SteamID.ToString());
        controllerState.IsFetching = false;
        Server.NextWorldUpdate(() =>
        {
            if (response == null)
            {
                player?.PrintToChat(Localizer["invsim.login_failed"]);
                return;
            }
            player?.PrintToChat(
                Localizer[
                    "invsim.login",
                    $"{Api.GetUrl("/api/sign-in/callback")}?token={response.Token}"
                ]
            );
        });
    }

    // ========================================================================
    // Configuration & File Management
    // ========================================================================

    public void HandleFileChanged(object? _, string value)
    {
        if (Inventories.Load(value))
            foreach (var player in Utilities.GetPlayers().Where(p => !p.IsBot))
                if (Inventories.TryGet(player.SteamID, out var inventory))
                    player.GetState().Inventory = inventory;
    }

    public void HandleIsRequireInventoryChanged(object? _, bool value)
    {
        if (ConVars.IsRequireInventory.Value)
            Natives.CServerSideClientBase_ActivatePlayer.Hook(OnActivatePlayerPre, HookMode.Pre);
        else
            Natives.CServerSideClientBase_ActivatePlayer.Unhook(OnActivatePlayerPre, HookMode.Pre);
    }

    // ========================================================================
    // Runtime: Round Win Case Reward
    // ========================================================================

    private static readonly Random _roundWinRandom = new();

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
                        player.PrintToChat(
                            CSS.Plugin.Localizer["invsim.roundwin_case", ConVars.ChatPrefix.Value]
                        );
                });
                return;
            }
        }

        await Api.SendAddContainer(userId);
        Server.NextWorldUpdate(() =>
        {
            if (player.IsValid)
                player.PrintToChat(
                    CSS.Plugin.Localizer["invsim.roundwin_case", ConVars.ChatPrefix.Value]
                );
        });
    }

    // ========================================================================
    // Runtime: Auto-Reload (change detection)
    // ========================================================================

    public static async void HandleAutoReloadForPlayer(CCSPlayerController player)
    {
        if (!player.IsValid || player.IsBot)
            return;
        var state = player.GetState();
        if (state.IsFetching || state.IsAutoReloading || state.IsLoadedFromFile)
            return;
        state.IsAutoReloading = true;
        try
        {
            var response = await Api.FetchEquipped(player.SteamID);
            if (response == null)
                return;
            var newInventory = new PlayerInventory(response);
            var newFingerprint = newInventory.ComputeFingerprint();

            if (state.InventoryFingerprint == null)
            {
                state.InventoryFingerprint = newFingerprint;
                return;
            }
            if (state.InventoryFingerprint == newFingerprint)
                return;
            state.InventoryFingerprint = newFingerprint;

            var oldInventory = state.Inventory;
            newInventory.InitializeWearOverrides();
            if (oldInventory != null)
                newInventory.WeaponWearCache = oldInventory.WeaponWearCache;
            state.WsUpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            state.Inventory = newInventory;
            Server.NextWorldUpdate(() =>
            {
                if (!player.IsValid)
                    return;
                HandlePlayerInventoryLoad(player);
                player.RegiveAgent(newInventory, oldInventory);
                player.RegiveGloves(newInventory, oldInventory);
                player.RegiveWeapons(newInventory, oldInventory);
            });
        }
        finally
        {
            state.IsAutoReloading = false;
        }
    }

    public void HandleAutoReload()
    {
        if (!ConVars.IsAutoReloadEnabled.Value)
            return;
        foreach (var player in Utilities.GetPlayers().Where(p => !p.IsBot && p.IsValid))
            HandleAutoReloadForPlayer(player);
    }

    // ========================================================================
    // Cleanup & Disconnection
    // ========================================================================

    public static void HandleControllerDeleted(CCSPlayerController controller)
    {
        LastBroadcastedCaseOpenedAt.TryRemove(controller.SteamID, out _);
        LastBroadcastedTradeUpCompletedAt.TryRemove(controller.SteamID, out _);
        PlayerJoinTimeUtc.TryRemove(controller.SteamID, out _);
        controller.RemoveState();
    }
}
