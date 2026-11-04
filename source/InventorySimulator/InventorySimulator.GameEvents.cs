/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Ian Lucas. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace InventorySimulator;

public partial class InventorySimulator
{
    public HookResult OnPlayerConnect(EventPlayerConnect @event, GameEventInfo _)
    {
        var player = @event.Userid;
        if (player != null && !player.IsBot)
            player.HandleConnect();
        return HookResult.Continue;
    }

    public HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo _)
    {
        var player = @event.Userid;
        if (player != null && !player.IsBot)
        {
            player.HandleConnect();
            RecordPlayerJoinTime(player);
            player.GetState().IsRoundWinCasesDisabled = NoCasesPersistence.IsOptedOut(
                player.SteamID
            );
        }
        return HookResult.Continue;
    }

    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo _)
    {
        var player = @event.Userid;
        if (player != null && !player.IsBot)
            player.HandleSpawn();
        return HookResult.Continue;
    }

    public HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo _)
    {
        var player = @event.Userid;
        if (
            player == null
            || player.IsBot
            || @event.Disconnect
            || @event.Team == @event.Oldteam
        )
            return HookResult.Continue;
        // Next frame is too early: the game applies the old coin after that.
        // Wait until TeamNum has settled, then do the same inventory refresh !ws uses.
        Runtime.Plugin.AddTimer(
            0.2f,
            () =>
            {
                if (player.IsValid)
                    player.RefreshSideCosmetics();
            }
        );
        return HookResult.Continue;
    }

    public HookResult OnPlayerDeathPre(EventPlayerDeath @event, GameEventInfo _)
    {
        var attacker = @event.Attacker;
        var victim = @event.Userid;
        if (attacker != null && victim != null)
        {
            var isAttackerValid = !attacker.IsBot && attacker.IsValid;
            var isVictimValid =
                (!ConVars.IsStatTrakIgnoreBots.Value || !victim.IsBot) && victim.IsValid;
            if (isAttackerValid && isVictimValid)
                attacker.IncrementWeaponStatTrak(@event.Weapon, @event.WeaponItemid);
        }
        return HookResult.Continue;
    }

    public HookResult OnRoundMvpPre(EventRoundMvp @event, GameEventInfo _)
    {
        var player = @event.Userid;
        if (player != null && !player.IsBot && player.IsValid)
            player.IncrementMusicKitStatTrak(@event);
        return HookResult.Continue;
    }

    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo _)
    {
        if (!ConVars.IsRoundWinEnabled.Value)
            return HookResult.Continue;
        var winnerTeam = @event.Winner;
        foreach (
            var player in Utilities
                .GetPlayers()
                .Where(p => !p.IsBot && p.IsValid && p.TeamNum == winnerTeam)
        )
        {
            if (!player.GetState().IsRoundWinCasesDisabled)
                HandleRoundWinCaseReward(player);
        }
        return HookResult.Continue;
    }

    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo _)
    {
        var player = @event.Userid;
        if (player != null && !player.IsBot)
            player.HandleDisconnect();
        return HookResult.Continue;
    }
}
