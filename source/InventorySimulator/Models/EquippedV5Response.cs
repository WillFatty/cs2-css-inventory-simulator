/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Ian Lucas. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System.Text.Json;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Modules.Utils;

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

    [JsonPropertyName("trails")]
    public EquippedTrailsResponse? Trails { get; set; }
}

public class EquippedTrailsResponse
{
    /// <summary>Team-keyed hex colors of the equipped nade trails.</summary>
    [JsonPropertyName("nade")]
    [JsonConverter(typeof(TeamTrailColorDictionaryConverter))]
    public Dictionary<byte, string>? Nade { get; set; }

    /// <summary>Team-keyed hex colors of the equipped player trails.</summary>
    [JsonPropertyName("player")]
    [JsonConverter(typeof(TeamTrailColorDictionaryConverter))]
    public Dictionary<byte, string>? Player { get; set; }
}

/// <summary>
/// Accepts the current team-keyed trail map or the legacy single color,
/// which applies to both teams.
/// </summary>
public class TeamTrailColorDictionaryConverter : JsonConverter<Dictionary<byte, string>>
{
    public override Dictionary<byte, string> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType == JsonTokenType.Null)
            return [];

        if (reader.TokenType == JsonTokenType.String)
        {
            var color = reader.GetString();
            return string.IsNullOrWhiteSpace(color)
                ? []
                : new Dictionary<byte, string>
                {
                    [(byte)CsTeam.Terrorist] = color,
                    [(byte)CsTeam.CounterTerrorist] = color,
                };
        }

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected string or object for trail colors.");

        using var document = JsonDocument.ParseValue(ref reader);
        var result = new Dictionary<byte, string>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (
                !byte.TryParse(property.Name, out var team)
                || property.Value.ValueKind != JsonValueKind.String
            )
                continue;
            var color = property.Value.GetString();
            if (!string.IsNullOrWhiteSpace(color))
                result[team] = color;
        }
        return result;
    }

    public override void Write(
        Utf8JsonWriter writer,
        Dictionary<byte, string> value,
        JsonSerializerOptions options
    )
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}
