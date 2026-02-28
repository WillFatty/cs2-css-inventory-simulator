/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Ian Lucas. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System.Collections.Concurrent;
using System.Text.Json;
using CounterStrikeSharp.API;
using Microsoft.Extensions.Logging;

namespace InventorySimulator;

public static class NoCasesPersistence
{
    private static readonly ConcurrentDictionary<ulong, byte> _optedOut = new();
    private static readonly string _configDir =
        "csgo/addons/counterstrikesharp/configs/plugins/InventorySimulator";
    private const string FileName = "nocases_steamids.json";

    public static void Load()
    {
        try
        {
            var path = Path.Combine(Server.GameDirectory, _configDir, FileName);
            if (!File.Exists(path))
                return;
            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<string>>(json);
            _optedOut.Clear();
            if (list != null)
                foreach (var s in list)
                    if (ulong.TryParse(s, out var steamId))
                        _optedOut.TryAdd(steamId, 0);
        }
        catch (Exception ex)
        {
            CSS.Plugin.Logger.LogError("Failed to load nocases list: {Message}", ex.Message);
        }
    }

    public static bool IsOptedOut(ulong steamId) => _optedOut.ContainsKey(steamId);

    /// <summary>Toggles opt-out for the given SteamID. Returns true if opted out after toggle.</summary>
    public static bool Toggle(ulong steamId)
    {
        if (_optedOut.TryRemove(steamId, out _))
        {
            Save();
            return false;
        }
        _optedOut.TryAdd(steamId, 0);
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
            var list = _optedOut.Keys.Select(k => k.ToString()).ToList();
            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            CSS.Plugin.Logger.LogError("Failed to save nocases list: {Message}", ex.Message);
        }
    }
}
