/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Ian Lucas. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System.Text.Json.Serialization;

namespace InventorySimulator;

public class AddItemInventoryItem
{
    [JsonPropertyName("id")]
    public required int Id { get; set; }
}

public class AddItemRequest
{
    [JsonPropertyName("apiKey")]
    public required string ApiKey { get; set; }

    [JsonPropertyName("userId")]
    public required string UserId { get; set; }

    [JsonPropertyName("inventoryItem")]
    public required AddItemInventoryItem InventoryItem { get; set; }
}
