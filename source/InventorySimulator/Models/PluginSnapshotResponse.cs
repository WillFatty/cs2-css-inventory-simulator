/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Ian Lucas. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System.Text.Json.Serialization;

namespace InventorySimulator;

public class PluginSnapshotUserEntry
{
    [JsonPropertyName("lastCaseOpening")]
    public LastCaseOpeningResponse? LastCaseOpening { get; set; }

    [JsonPropertyName("lastTradeUp")]
    public LastTradeUpResponse? LastTradeUp { get; set; }

    [JsonPropertyName("equipped")]
    public EquippedV4Response? Equipped { get; set; }

    [JsonPropertyName("syncedAt")]
    public long SyncedAt { get; set; }
}
