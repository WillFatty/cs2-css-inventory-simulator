/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Ian Lucas. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace InventorySimulator;

public class PlayerInventory(EquippedV5Response data)
{
    private readonly EquippedV5Response _data = data;
    public Dictionary<byte, InventoryItem> Agents => _data.Agents;
    public InventoryItem? Pet => _data.Pet;

    public static PlayerInventory Empty() => new(new());

    public IEnumerable<InventoryItem> GetAllWeapons() =>
        _data.Knives.Values.Concat(_data.CTWeapons.Values).Concat(_data.TWeapons.Values);

    public void InitializeWearOverrides()
    {
        foreach (var item in GetAllWeapons())
            item.WearOverride = WearRegistry.Resolve(item);
    }

    public InventoryItem? GetKnife(byte team, bool fallback)
    {
        if (_data.Knives.TryGetValue(team, out var knife))
            return knife;
        if (fallback && _data.Knives.TryGetValue(TeamHelper.ToggleTeam(team), out knife))
            return knife;
        return null;
    }

    public Dictionary<ushort, InventoryItem> GetWeapons(byte team)
    {
        return (CsTeam)team == CsTeam.Terrorist ? _data.TWeapons : _data.CTWeapons;
    }

    public InventoryItem? GetWeapon(byte team, ushort def, bool fallback)
    {
        if (GetWeapons(team).TryGetValue(def, out var weapon))
            return weapon;
        if (fallback && GetWeapons(TeamHelper.ToggleTeam(team)).TryGetValue(def, out weapon))
            return weapon;
        return null;
    }

    public InventoryItem? GetGloves(byte team, bool fallback)
    {
        if (_data.Gloves.TryGetValue(team, out var glove))
            return glove;
        if (fallback && _data.Gloves.TryGetValue(TeamHelper.ToggleTeam(team), out glove))
            return glove;
        return null;
    }

    public InventoryItem? GetMusicKit(byte team, bool fallback = false) =>
        GetTeamCosmetic(_data.MusicKit, team, fallback);

    public InventoryItem? GetCollectible(byte team, bool fallback = false) =>
        GetTeamCosmetic(_data.Collectible, team, fallback);

    public InventoryItem? GetGraffiti(byte team, bool fallback = false) =>
        GetTeamCosmetic(_data.Graffiti, team, fallback);

    private static InventoryItem? GetTeamCosmetic(
        Dictionary<byte, InventoryItem>? items,
        byte team,
        bool fallback
    )
    {
        if (items == null || items.Count == 0)
            return null;
        if (items.TryGetValue(team, out var item))
            return item;
        if (
            fallback
            && team is (byte)CsTeam.Terrorist or (byte)CsTeam.CounterTerrorist
            && items.TryGetValue(TeamHelper.ToggleTeam(team), out item)
        )
            return item;
        return null;
    }

    public ushort? GetLoadoutDef(byte team, loadout_slot_t slot)
    {
        if (
            _data.Loadout != null
            && _data.Loadout.TryGetValue(team, out var slots)
            && slots.TryGetValue((byte)slot, out var def)
        )
            return def;
        return null;
    }

    // Visual identity only — StatTrak and the website `hash` (which embeds StatTrak)
    // must not be included, or every kill triggers a full skin re-give via live sync.
    private static string GetVisualKey(InventoryItem item)
    {
        var stickers =
            item.Stickers != null
                ? string.Join(
                    "_",
                    item.Stickers.Select(s => $"{s.Def},{s.Slot},{s.Wear},{s.X},{s.Y}")
                )
                : string.Empty;
        var keychains =
            item.Keychains != null
                ? string.Join("_", item.Keychains.Select(k => $"{k.Def},{k.Slot}"))
                : string.Empty;
        return $"{item.Uid}|{item.Def}|{item.Paint}|{item.Seed}|{item.Wear}|{item.Nametag}|{item.Style}|{item.Tint}|{item.Charges}|{item.UpgradeLevel}|{item.MusicId}|{item.PetId}|{stickers}|{keychains}";
    }

    public int ComputeFingerprint()
    {
        var keys = new List<string>();
        foreach (var item in _data.Agents.Values)
            keys.Add(GetVisualKey(item));
        foreach (var item in _data.Knives.Values)
            keys.Add(GetVisualKey(item));
        foreach (var item in _data.CTWeapons.Values)
            keys.Add(GetVisualKey(item));
        foreach (var item in _data.TWeapons.Values)
            keys.Add(GetVisualKey(item));
        foreach (var item in _data.Gloves.Values)
            keys.Add(GetVisualKey(item));
        foreach (var (team, item) in _data.MusicKit)
            keys.Add($"music|{team}|{GetVisualKey(item)}");
        foreach (var (team, item) in _data.Graffiti)
            keys.Add($"graffiti|{team}|{GetVisualKey(item)}");
        foreach (var (team, item) in _data.Collectible)
            keys.Add($"collectible|{team}|{GetVisualKey(item)}");
        if (_data.Pet != null)
            keys.Add(GetVisualKey(_data.Pet));
        if (_data.Loadout != null)
        {
            foreach (var (team, slots) in _data.Loadout.OrderBy(kv => kv.Key))
            foreach (var (slot, def) in slots.OrderBy(kv => kv.Key))
                keys.Add($"loadout|{team}|{slot}|{def}");
        }
        keys.Sort();
        return string.Join(",", keys).GetHashCode();
    }

    public InventoryItem? GetItemForSlot(
        byte team,
        loadout_slot_t slot,
        ushort def,
        bool fallback,
        int minModels = 0
    )
    {
        if (
            slot >= loadout_slot_t.LOADOUT_SLOT_MELEE
            && slot <= loadout_slot_t.LOADOUT_SLOT_EQUIPMENT5
        )
        {
            return slot == loadout_slot_t.LOADOUT_SLOT_MELEE
                ? GetKnife(team, fallback)
                : GetWeapon(team, def, fallback);
        }
        if (slot == loadout_slot_t.LOADOUT_SLOT_CLOTHING_CUSTOMPLAYER)
        {
            if (minModels > 0)
                return team == (byte)CsTeam.Terrorist
                    ? new InventoryItem { Def = 5036 }
                    : new InventoryItem { Def = 5037 };
            if (_data.Agents.TryGetValue(team, out var item))
                return item;
            return null;
        }
        if (slot == loadout_slot_t.LOADOUT_SLOT_CLOTHING_HANDS)
        {
            return GetGloves(team, fallback);
        }
        // Flair/music fall back across teams so a one-sided loadout still shows
        // in-game; when both sides are set, the current team wins.
        if (slot == loadout_slot_t.LOADOUT_SLOT_FLAIR0)
            return GetCollectible(team, fallback: true);
        if (slot == loadout_slot_t.LOADOUT_SLOT_MUSICKIT)
            return GetMusicKit(team, fallback: true);
        if (slot == loadout_slot_t.LOADOUT_SLOT_PET)
            return _data.Pet;
        return null;
    }

    public void ClearGraffiti(byte team)
    {
        _data.Graffiti.Remove(team);
    }
}
