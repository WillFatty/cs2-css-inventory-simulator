/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Ian Lucas. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System.Text.Json;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Modules.Utils;

namespace InventorySimulator;

/// <summary>
/// Accepts either the legacy singular item shape or the team-keyed dictionary
/// used for music kits, collectibles, and graffiti.
/// </summary>
public class TeamInventoryItemDictionaryConverter
    : JsonConverter<Dictionary<byte, InventoryItem>>
{
    public override Dictionary<byte, InventoryItem> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType == JsonTokenType.Null)
            return [];

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected object for team inventory item map.");

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var result = new Dictionary<byte, InventoryItem>();

        // Legacy singular payload: { "def": ..., "musicId": ... }
        if (LooksLikeInventoryItem(root))
        {
            var item = root.Deserialize<InventoryItem>(options);
            if (item != null)
            {
                result[(byte)CsTeam.CounterTerrorist] = item;
                result[(byte)CsTeam.Terrorist] = item;
            }
            return result;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!byte.TryParse(property.Name, out var team))
                continue;
            var item = property.Value.Deserialize<InventoryItem>(options);
            if (item != null)
                result[team] = item;
        }

        return result;
    }

    public override void Write(
        Utf8JsonWriter writer,
        Dictionary<byte, InventoryItem> value,
        JsonSerializerOptions options
    )
    {
        JsonSerializer.Serialize(writer, value, options);
    }

    private static bool LooksLikeInventoryItem(JsonElement element)
    {
        return element.TryGetProperty("def", out _)
            || element.TryGetProperty("musicId", out _)
            || element.TryGetProperty("uid", out _)
            || element.TryGetProperty("charges", out _);
    }
}
