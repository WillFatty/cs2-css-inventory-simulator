/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Ian Lucas. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;

namespace InventorySimulator;

public partial class InventorySimulator : BasePlugin
{
    public override string ModuleAuthor => "Ian Lucas";
    public override string ModuleDescription => "Inventory Simulator (inventory.cstrike.app)";
    public override string ModuleName => "InventorySimulator";
    public override string ModuleVersion => "1.0.0";

    public override void Load(bool hotReload)
    {
        CSS.Initialize(this);
        ConVars.Initialize(this);
        RegisterListener<Listeners.OnEntityCreated>(OnEntityCreated);
        RegisterListener<Listeners.OnEntityDeleted>(OnEntityDeleted);
        RegisterEventHandler<EventPlayerConnect>(OnPlayerConnect, HookMode.Post);
        RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull, HookMode.Post);
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeathPre);
        RegisterEventHandler<EventRoundMvp>(OnRoundMvpPre);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd, HookMode.Post);
        Natives.CCSPlayerController_ProcessUsercmds.Hook(OnProcessUsercmds, HookMode.Post);
        VirtualFunctions.GiveNamedItemFunc.Hook(OnGiveNamedItemPre, HookMode.Pre);
        Natives.CCSPlayerInventory_GetItemInLoadout.Hook(GetItemInLoadout, HookMode.Post);
        ConVars.File.ValueChanged += HandleFileChanged;
        ConVars.IsRequireInventory.ValueChanged += HandleIsRequireInventoryChanged;
        ConVars.AutoReloadInterval.ValueChanged += HandleAutoReloadIntervalChanged;
        ConVars.UnboxPollInterval.ValueChanged += HandleUnboxPollIntervalChanged;
        NoCasesPersistence.Load();
        HandleFileChanged(null, ConVars.File.Value);
        HandleIsRequireInventoryChanged(null, ConVars.IsRequireInventory.Value);
        HandleUnboxPollIntervalChanged(null, ConVars.UnboxPollInterval.Value);
        StartAutoReloadTimer();
        StartUnboxPollTimer();
        StartUnboxBroadcastDrainTimer();
    }

    private CounterStrikeSharp.API.Modules.Timers.Timer? _autoReloadTimer;

    private void StartAutoReloadTimer()
    {
        _autoReloadTimer?.Kill();
        _autoReloadTimer = AddTimer(
            Math.Max(1, ConVars.AutoReloadInterval.Value),
            HandleAutoReload,
            TimerFlags.REPEAT
        );
    }

    public void HandleAutoReloadIntervalChanged(object? _, int value)
    {
        StartAutoReloadTimer();
    }

    private CounterStrikeSharp.API.Modules.Timers.Timer? _unboxPollTimer;

    private void HandleUnboxPollIntervalChanged(object? _, float value)
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

    private CounterStrikeSharp.API.Modules.Timers.Timer? _unboxBroadcastDrainTimer;

    private void StartUnboxBroadcastDrainTimer()
    {
        _unboxBroadcastDrainTimer?.Kill();
        _unboxBroadcastDrainTimer = AddTimer(
            0.1f,
            () =>
            {
                DrainOneUnboxBroadcast();
            },
            TimerFlags.REPEAT
        );
    }

    public override void Unload(bool hotReload)
    {
        CCSPlayerControllerState.ClearAllEconItemView();
    }
}
