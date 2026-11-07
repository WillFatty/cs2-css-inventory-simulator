/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Ian Lucas. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System.Text.Json.Serialization;

namespace InventorySimulator;

public sealed class PointsStateResponse
{
    [JsonPropertyName("points")]
    public int Points { get; set; }

    [JsonPropertyName("rank")]
    public int? Rank { get; set; }
}

public sealed class PointsLeaderboardEntry
{
    [JsonPropertyName("steamId")]
    public string SteamId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("points")]
    public int Points { get; set; }

    [JsonPropertyName("rank")]
    public int Rank { get; set; }
}

public sealed class PointsLeaderboardResponse
{
    [JsonPropertyName("entries")]
    public List<PointsLeaderboardEntry> Entries { get; set; } = [];

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("totalEntries")]
    public int TotalEntries { get; set; }

    [JsonPropertyName("viewerRank")]
    public int? ViewerRank { get; set; }
}


