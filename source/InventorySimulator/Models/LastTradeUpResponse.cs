/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Ian Lucas. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System.Text.Json.Serialization;

namespace InventorySimulator;

public class LastTradeUpResponse
{
    [JsonPropertyName("userName")]
    public required string UserName { get; set; }

    [JsonPropertyName("outputItemName")]
    public required string OutputItemName { get; set; }

    [JsonPropertyName("completedAt")]
    public string? CompletedAt { get; set; }

    [JsonPropertyName("rarity")]
    public string? Rarity { get; set; }
}
