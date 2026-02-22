/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Ian Lucas. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System.Text.Json.Serialization;

namespace InventorySimulator;

public class LastCaseOpeningResponse
{
    [JsonPropertyName("userName")]
    public required string UserName { get; set; }

    [JsonPropertyName("caseName")]
    public required string CaseName { get; set; }

    [JsonPropertyName("unlockedItemName")]
    public required string UnlockedItemName { get; set; }

    [JsonPropertyName("openedAt")]
    public string? OpenedAt { get; set; }

    [JsonPropertyName("rarity")]
    public string? Rarity { get; set; }
}
