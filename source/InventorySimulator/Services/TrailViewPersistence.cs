/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Ian Lucas. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System.Collections.Concurrent;
using System.Text.Json;
using CounterStrikeSharp.API;
using Microsoft.Extensions.Logging;

namespace InventorySimulator;

public static class TrailViewPersistence
{
    private static readonly ConcurrentDictionary<ulong, byte> _nadeOptOut = new();
    private static readonly ConcurrentDictionary<ulong, byte> _playerOptOut = new();
    private static readonly string _configDir =
        "csgo/addons/counterstrikesharp/configs/plugins/InventorySimulator";
    private const string FileName = "trail_view_optouts.json";

    public static bool HasNadeOptOuts => !_nadeOptOut.IsEmpty;
    public static bool HasPlayerOptOuts => !_playerOptOut.IsEmpty;

    public static void Load()
    {
        try
        {
            var path = Path.Combine(Server.GameDirectory, _configDir, FileName);
            if (!File.Exists(path))
                return;

            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<TrailViewOptOutFile>(json);
            _nadeOptOut.Clear();
            _playerOptOut.Clear();
            LoadIds(_nadeOptOut, data?.Nade);
            LoadIds(_playerOptOut, data?.Player);
        }
        catch (Exception ex)
        {
            Runtime.Plugin.Logger.LogError(
                "Failed to load trail view opt-outs: {Message}",
                ex.Message
            );
        }
    }

    public static bool IsNadeOptedOut(ulong steamId) => _nadeOptOut.ContainsKey(steamId);

    public static bool IsPlayerOptedOut(ulong steamId) => _playerOptOut.ContainsKey(steamId);

    /// <summary>Toggles nade-trail opt-out. Returns true if opted out after toggle.</summary>
    public static bool ToggleNade(ulong steamId) => Toggle(_nadeOptOut, steamId);

    /// <summary>Toggles player-trail opt-out. Returns true if opted out after toggle.</summary>
    public static bool TogglePlayer(ulong steamId) => Toggle(_playerOptOut, steamId);

    private static void LoadIds(ConcurrentDictionary<ulong, byte> map, List<string>? ids)
    {
        if (ids is null)
            return;

        foreach (var s in ids)
            if (ulong.TryParse(s, out var steamId))
                map.TryAdd(steamId, 0);
    }

    private static bool Toggle(ConcurrentDictionary<ulong, byte> map, ulong steamId)
    {
        if (map.TryRemove(steamId, out _))
        {
            Save();
            return false;
        }

        map.TryAdd(steamId, 0);
        Save();
        return true;
    }

    private static void Save()
    {
        try
        {
            var dir = Path.Combine(Server.GameDirectory, _configDir);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, FileName);
            var data = new TrailViewOptOutFile
            {
                Nade = _nadeOptOut.Keys.Select(k => k.ToString()).ToList(),
                Player = _playerOptOut.Keys.Select(k => k.ToString()).ToList(),
            };
            var json = JsonSerializer.Serialize(
                data,
                new JsonSerializerOptions { WriteIndented = false }
            );
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Runtime.Plugin.Logger.LogError(
                "Failed to save trail view opt-outs: {Message}",
                ex.Message
            );
        }
    }

    private sealed class TrailViewOptOutFile
    {
        public List<string> Nade { get; set; } = [];
        public List<string> Player { get; set; } = [];
    }
}
