/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Ian Lucas. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;

namespace InventorySimulator;

public partial class InventorySimulator
{
    public HookResult OnActivatePlayerPre(DynamicHook hook)
    {
        var thisPtr = hook.GetParam<nint>(0);
        var userid = new CServerSideClientBase(thisPtr).UserID;
        var player = Utilities.GetPlayerFromUserid(userid);
        if (player != null && !player.IsBot)
        {
            player.Revalidate();
            var controllerState = player.GetState();
            if (controllerState.Inventory == null)
            {
                controllerState.PostFetchCallback = () =>
                    Server.NextWorldUpdate(() =>
                    {
                        if (player.IsValid)
                            Natives.CServerSideClientBase_ActivatePlayer.Invoke(thisPtr);
                    });
                if (!controllerState.IsFetching)
                    player.RefreshInventory();
                return HookResult.Stop;
            }
        }
        return HookResult.Continue;
    }

    public HookResult OnGiveNamedItemPre(DynamicHook hook)
    {
        var itemServices = hook.GetParam<CCSPlayer_ItemServices>(0);
        var designerName = hook.GetParam<string>(1);
        var controller = itemServices.GetController();
        if (controller?.SteamID != 0 && controller?.InventoryServices != null)
        {
            var itemDef = SchemaHelper.GetItemSchema()?.GetItemDefinitionByName(designerName);
            if (itemDef != null)
            {
                var controllerState = controller.GetState();
                var item = controllerState.Inventory?.GetItemForSlot(
                    controller.TeamNum,
                    itemDef.DefaultLoadoutSlot,
                    itemDef.DefIndex,
                    ConVars.IsFallbackTeam.Value
                );
                if (item != null)
                {
                    var customView = controllerState.GetEconItemView(
                        controller.TeamNum,
                        (int)itemDef.DefaultLoadoutSlot,
                        item
                    );
                    if (customView != nint.Zero)
                        hook.SetParam(3, customView);
                }
            }
        }
        return HookResult.Continue;
    }

    public HookResult GetItemInLoadout(DynamicHook hook)
    {
        var team = hook.GetParam<int>(1);
        var slot = hook.GetParam<int>(2);
        // Same as an unequipped slot, the game's base item for pets is empty.
        if (slot == (int)loadout_slot_t.LOADOUT_SLOT_PET && !ConVars.IsPetEnabled.Value)
        {
            hook.SetReturn(
                SchemaHelper
                    .GetInventoryManager()
                    .GetBaseItemForTeam((byte)team, (loadout_slot_t)slot)
            );
            return HookResult.Changed;
        }
        var inventory = new CCSPlayerInventory(hook.GetParam<nint>(0));
        if (!inventory.IsValid)
            return HookResult.Continue;
        var ret = hook.GetReturn<nint>();
        if (ret == nint.Zero)
            return HookResult.Continue;
        var itemView = new CEconItemView(ret);
        var player = PlayerHelper.GetPlayerFromSteamId(inventory.SOCache.Owner.SteamID);
        if (player == null)
            return HookResult.Continue;
        var controllerState = player.GetState();
        var def = itemView.ItemDefinitionIndex;
        // Flair and music kits are not team slots in the game, so it asks with team 0.
        // Use the player's current side or a team switch keeps the previous coin.
        var lookupTeam = ResolveCosmeticTeam(player, team, slot);
        var loadoutDef = ConVars.IsLoadoutEnabled.Value
            ? controllerState.Inventory?.GetLoadoutDef((byte)lookupTeam, (loadout_slot_t)slot)
            : null;
        var item = controllerState.Inventory?.GetItemForSlot(
            (byte)lookupTeam,
            (loadout_slot_t)slot,
            loadoutDef ?? def,
            ConVars.IsFallbackTeam.Value,
            ConVars.MinModels.Value
        );
        // Buying, the buy menu and the spawn weapons all read the weapon from here. The player
        // may have it equipped in another slot of their game loadout, otherwise the game
        // returns this same stock item when a default-equipped weapon has no skin.
        if (item == null && loadoutDef != null && loadoutDef != def)
        {
            var gameItem = inventory.FindWeaponInLoadout((byte)team, loadoutDef.Value);
            if (gameItem == nint.Zero)
                gameItem = SchemaHelper
                    .GetInventoryManager()
                    .GetReferenceEconItem(loadoutDef.Value);
            if (gameItem == nint.Zero)
                return HookResult.Continue;
            hook.SetReturn(gameItem);
            return HookResult.Changed;
        }
        if (item != null)
        {
            var customView = controllerState.GetEconItemView(
                lookupTeam,
                slot,
                item,
                itemView.Handle
            );
            if (customView != nint.Zero)
            {
                hook.SetReturn(customView);
                return HookResult.Changed;
            }
        }
        return HookResult.Continue;
    }

    static int ResolveCosmeticTeam(CCSPlayerController player, int team, int slot)
    {
        if (
            slot != (int)loadout_slot_t.LOADOUT_SLOT_FLAIR0
            && slot != (int)loadout_slot_t.LOADOUT_SLOT_MUSICKIT
        )
            return team;
        if (team is (int)CsTeam.Terrorist or (int)CsTeam.CounterTerrorist)
            return team;
        var playerTeam = player.TeamNum;
        return playerTeam is (byte)CsTeam.Terrorist or (byte)CsTeam.CounterTerrorist
            ? playerTeam
            : team;
    }

    public HookResult OnChickenManagerServerGamePostSimulate(DynamicHook hook)
    {
        if (
            !ConVars.IsPetFreeRoam.Value
            && !(ConVars.IsPetRespawn.Value && EntityHelper.IsWarmupPeriod())
        )
            return HookResult.Continue;
        foreach (var player in Utilities.GetPlayers())
        {
            var chicken = player.GetPetChicken();
            if (chicken != null && !chicken.CanRoam)
                chicken.CanRoam = true;
        }
        return HookResult.Continue;
    }
}
