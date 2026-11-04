/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Ian Lucas. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System.Text.Json.Serialization;

namespace InventorySimulator;

public class EquippedV5Response
{
    [JsonPropertyName("agents")]
    public Dictionary<byte, InventoryItem> Agents { get; set; } = [];

    [JsonPropertyName("collectible")]
    [JsonConverter(typeof(TeamInventoryItemDictionaryConverter))]
    public Dictionary<byte, InventoryItem> Collectible { get; set; } = [];

    [JsonPropertyName("ctWeapons")]
    public Dictionary<ushort, InventoryItem> CTWeapons { get; set; } = [];

    [JsonPropertyName("gloves")]
    public Dictionary<byte, InventoryItem> Gloves { get; set; } = [];

    [JsonPropertyName("graffiti")]
    [JsonConverter(typeof(TeamInventoryItemDictionaryConverter))]
    public Dictionary<byte, InventoryItem> Graffiti { get; set; } = [];

    [JsonPropertyName("knives")]
    public Dictionary<byte, InventoryItem> Knives { get; set; } = [];

    [JsonPropertyName("loadout")]
    public Dictionary<byte, Dictionary<byte, ushort>>? Loadout { get; set; }

    [JsonPropertyName("musicKit")]
    [JsonConverter(typeof(TeamInventoryItemDictionaryConverter))]
    public Dictionary<byte, InventoryItem> MusicKit { get; set; } = [];

    [JsonPropertyName("pet")]
    public InventoryItem? Pet { get; set; }

    [JsonPropertyName("tWeapons")]
    public Dictionary<ushort, InventoryItem> TWeapons { get; set; } = [];
}
