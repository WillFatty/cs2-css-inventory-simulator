/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Ian Lucas. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace InventorySimulator;

public partial class InventorySimulator
{
    [ConsoleCommand(
        "css_ws",
        "Refreshes player inventory from the Inventory Simulator service and displays the configured URL."
    )]
    public void OnWSCommand(CCSPlayerController? player, CommandInfo commandInfo) =>
        ExecuteInventoryRefreshCommand(player, commandInfo);

    [ConsoleCommand("css_knife", "Refreshes player's inventory (same as !ws).")]
    public void OnKnifeCommand(CCSPlayerController? player, CommandInfo commandInfo) =>
        ExecuteInventoryRefreshCommand(player, commandInfo);

    [ConsoleCommand("css_gloves", "Refreshes player's inventory (same as !ws).")]
    public void OnGlovesCommand(CCSPlayerController? player, CommandInfo commandInfo) =>
        ExecuteInventoryRefreshCommand(player, commandInfo);

    [ConsoleCommand("css_agents", "Refreshes player's inventory (same as !ws).")]
    public void OnAgentsCommand(CCSPlayerController? player, CommandInfo commandInfo) =>
        ExecuteInventoryRefreshCommand(player, commandInfo);

    private void ExecuteInventoryRefreshCommand(CCSPlayerController? player, CommandInfo _)
    {
        var prefix = Rules.GetChatPrefix();
        var url = UrlHelper.FormatUrl(ConVars.WsUrlPrintFormat.Value, ConVars.Url.Value);
        player?.PrintToChat(Localizer["invsim.announce", prefix, url]);
        if (!ConVars.IsWsEnabled.Value || player == null)
            return;
        var controllerState = player.GetState();
        var cooldown = ConVars.WsCooldown.Value;
        var diff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - controllerState.WsUpdatedAt;
        if (diff < cooldown)
        {
            player.PrintToChat(Localizer["invsim.ws_cooldown", prefix, cooldown - diff]);
            return;
        }
        if (controllerState.IsFetching)
        {
            player.PrintToChat(Localizer["invsim.ws_in_progress", prefix]);
            return;
        }
        player.RefreshInventory(force: true);
        player.PrintToChat(Localizer["invsim.ws_new", prefix]);
    }

    [ConsoleCommand(
        "css_spray",
        "Applies the player's equipped graffiti spray at their current location."
    )]
    public void OnSprayCommand(CCSPlayerController? player, CommandInfo _)
    {
        if (player != null && ConVars.IsSprayEnabled.Value)
        {
            var controllerState = player.GetState();
            var cooldown = ConVars.SprayCooldown.Value;
            var diff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - controllerState.SprayUsedAt;
            if (diff < cooldown)
            {
                player.PrintToChat(
                    Localizer["invsim.spray_cooldown", Rules.GetChatPrefix(), cooldown - diff]
                );
                return;
            }
            player.SprayGraffiti();
        }
    }

    [ConsoleCommand(
        "css_nocases",
        "Toggle round-win case drops for yourself (saved across sessions)."
    )]
    public void OnNoCasesCommand(CCSPlayerController? player, CommandInfo _)
    {
        if (player == null || player.IsBot)
            return;
        var isNowDisabled = NoCasesPersistence.Toggle(player.SteamID);
        player.GetState().IsRoundWinCasesDisabled = isNowDisabled;
        var prefix = Rules.GetChatPrefix();
        player.PrintToChat(
            isNowDisabled
                ? Localizer["invsim.nocases_disabled", prefix]
                : Localizer["invsim.nocases_enabled", prefix]
        );
    }

    [ConsoleCommand(
        "css_hideunbox",
        "Toggle case opening and trade-up chat messages for yourself (this session only)."
    )]
    public void OnHideUnboxCommand(CCSPlayerController? player, CommandInfo _)
    {
        if (player == null || player.IsBot)
            return;
        var state = player.GetState();
        state.HideUnboxTradeUpMessages = !state.HideUnboxTradeUpMessages;
        var prefix = Rules.GetChatPrefix();
        player.PrintToChat(
            state.HideUnboxTradeUpMessages
                ? Localizer["invsim.hideunbox_disabled", prefix]
                : Localizer["invsim.hideunbox_enabled", prefix]
        );
    }

    [ConsoleCommand(
        "css_wslogin",
        "Authenticates the player with Inventory Simulator and displays their login URL."
    )]
    public void OnWsloginCommand(CCSPlayerController? player, CommandInfo _)
    {
        if (ConVars.IsWsLogin.Value && Api.HasApiKey() && player != null)
        {
            var controllerState = player.GetState();
            player.PrintToChat(Localizer["invsim.login_in_progress", Rules.GetChatPrefix()]);
            if (controllerState.IsAuthenticating)
                return;
            player.SignIn();
        }
    }

    [ConsoleCommand(
        "css_inventorypanorama",
        "Toggle your Panorama case/trade-up notifications (!inventorypanorama [0|1])"
    )]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnPanoramaCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || player.IsBot || player.IsHLTV)
            return;

        var steamId = player.AuthorizedSteamID?.SteamId64;
        if (steamId is null)
            return;

        var currentlyEnabled = ConVars.UsePanoramaUI.Value && !_panoramaOptOut.Contains(steamId.Value);
        if (!TryParseEnableArg(command.GetArg(1), currentlyEnabled, out var enabled, out var error))
        {
            command.ReplyToCommand(error);
            return;
        }

        if (enabled)
            _panoramaOptOut.Remove(steamId.Value);
        else
            _panoramaOptOut.Add(steamId.Value);

        if (enabled && !ConVars.UsePanoramaUI.Value)
        {
            player.PrintToChat($" {ReplaceColorTags(Localizer["invsim.panorama_disabled_server", Rules.GetChatPrefix()])}");
            return;
        }

        player.PrintToChat(enabled
            ? $" {ReplaceColorTags(Localizer["invsim.panorama_enabled", Rules.GetChatPrefix()])}"
            : $" {ReplaceColorTags(Localizer["invsim.panorama_disabled", Rules.GetChatPrefix()])}");
    }

    /// <summary>
    /// Parses 0/1, on/off, true/false. Empty arg toggles <paramref name="current"/>.
    /// </summary>
    private static bool TryParseEnableArg(string arg, bool current, out bool enabled, out string error)
    {
        enabled = current;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(arg))
        {
            enabled = !current;
            return true;
        }

        switch (arg.Trim().ToLowerInvariant())
        {
            case "1":
            case "on":
            case "true":
            case "enable":
            case "enabled":
                enabled = true;
                return true;
            case "0":
            case "off":
            case "false":
            case "disable":
            case "disabled":
                enabled = false;
                return true;
            default:
                error = "Usage: !inventorypanorama [0|1]";
                return false;
        }
    }

    private static string ReplaceColorTags(string message)
    {
        return message
            .Replace("{default}", $"{ChatColors.Default}")
            .Replace("{white}", $"{ChatColors.White}")
            .Replace("{darkred}", $"{ChatColors.DarkRed}")
            .Replace("{green}", $"{ChatColors.Green}")
            .Replace("{lightyellow}", $"{ChatColors.LightYellow}")
            .Replace("{lightblue}", $"{ChatColors.LightBlue}")
            .Replace("{olive}", $"{ChatColors.Olive}")
            .Replace("{lime}", $"{ChatColors.Lime}")
            .Replace("{red}", $"{ChatColors.Red}")
            .Replace("{lightpurple}", $"{ChatColors.LightPurple}")
            .Replace("{purple}", $"{ChatColors.Purple}")
            .Replace("{grey}", $"{ChatColors.Grey}")
            .Replace("{yellow}", $"{ChatColors.Yellow}")
            .Replace("{gold}", $"{ChatColors.Gold}")
            .Replace("{silver}", $"{ChatColors.Silver}")
            .Replace("{blue}", $"{ChatColors.Blue}")
            .Replace("{darkblue}", $"{ChatColors.DarkBlue}")
            .Replace("{bluegrey}", $"{ChatColors.BlueGrey}")
            .Replace("{magenta}", $"{ChatColors.Magenta}")
            .Replace("{lightred}", $"{ChatColors.LightRed}")
            .Replace("{orange}", $"{ChatColors.Orange}");
    }
}
