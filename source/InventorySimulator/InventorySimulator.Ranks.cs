/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Ian Lucas. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System.Collections.Concurrent;
using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using HexiumCSUI;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace InventorySimulator;

public partial class InventorySimulator
{
    private const int RankLeaderboardPageSize = 10;
    private const int NadeTrailAttachMaxAttempts = 12;
    private const float TrailMinSegmentDistance = 3.0f;
    private const float TrailBeamLifetime = 1.25f;
    private const float TrailBeamWidth = 1.5f;

    // Premier rank type used by CS2 scoreboard rating display.
    private const sbyte PremierRankType = 11;

    private static readonly ConcurrentDictionary<ulong, RankPlayerState> _rankPlayers = new();
    private readonly ConcurrentDictionary<ulong, RankTrailColors> _playersWithNadeTrail = new();
    private readonly ConcurrentDictionary<ulong, RankTrailColors> _playersWithPlayerTrail = new();
    private readonly Dictionary<ulong, Vector> _playerTrailLastOrigin = new();
    private readonly Dictionary<uint, NadeTrailTracker> _nadeTrailTrackers = new();
    private readonly HashSet<CBeam> _nadeTrailBeams = [];
    private readonly HashSet<CBeam> _playerTrailBeams = [];
    private Timer? _rankAutosaveTimer;

    private enum TrailKind
    {
        Nade,
        Player,
    }

    private bool RanksActive => ConVars.IsRankEnabled.Value && Api.HasApiKey();

    private sealed class RankPlayerState
    {
        public required ulong SteamId { get; init; }
        public string Name = "";
        public int ServerPoints;
        public int PendingDelta;
        public bool IsLoaded;
        public int Points => ServerPoints + PendingDelta;
    }

    public void InitializeRanksFeatures(bool hotReload)
    {
        RegisterListener<Listeners.OnTick>(OnRanksTick);
        RegisterListener<Listeners.OnMapEnd>(OnRanksMapEnd);
        RegisterListener<Listeners.CheckTransmit>(OnRanksCheckTransmit);
        _rankAutosaveTimer = AddTimer(30.0f, FlushDirtyRankPoints, TimerFlags.REPEAT);


        if (ConVars.IsRankEnabled.Value && !Api.HasApiKey())
            Logger.LogWarning(
                "invsim_rank_enabled is on but invsim_apikey is empty — ranks features are disabled until an API key is set."
            );

        if (hotReload)
            Server.NextWorldUpdate(() =>
            {
                foreach (var player in Utilities.GetPlayers().Where(p => !p.IsBot))
                    HandleRankPlayerConnect(player);
            });
    }

    public void UnloadRanksFeatures()
    {
        _rankAutosaveTimer?.Kill();
        _rankAutosaveTimer = null;
        ClearRankTrails();
        _playersWithNadeTrail.Clear();
        _playersWithPlayerTrail.Clear();
        _playerTrailLastOrigin.Clear();
        FlushAllRankPoints();
    }

    private void OnRanksTick()
    {
        if (!RanksActive)
            return;

        UpdateNadeTrailBeams();
        UpdateRankPlayerTrails();

        // CS2 resets the competitive rank fields every frame, so this must run
        // every tick — throttling makes the rank flash on the scoreboard.
        if (!ConVars.RankShowPremierOnScoreboard.Value)
            return;

        foreach (var player in Utilities.GetPlayers())
        {
            if (!IsRankPlayer(player))
                continue;

            if (
                _rankPlayers.TryGetValue(player.SteamID, out var state)
                && state.IsLoaded
            )
                ApplyRankScoreboard(player, state.Points);
        }
    }

    private void OnRanksMapEnd()
    {
        FlushAllRankPoints();
        ClearRankTrails();
        _playerTrailLastOrigin.Clear();
    }

    private void HandleRankPlayerConnect(CCSPlayerController player)
    {
        if (!RanksActive || !IsRankPlayer(player))
            return;

        var steamId = player.SteamID;
        if (steamId == 0)
            return;

        var state = _rankPlayers.GetOrAdd(
            steamId,
            id => new RankPlayerState { SteamId = id }
        );
        state.Name = player.PlayerName;

        var userId = steamId.ToString();
        var name = player.PlayerName;
        _ = Task.Run(async () =>
        {
            try
            {
                var response = await Api.FetchPointsStateAsync(
                    userId,
                    name,
                    ConVars.RankStartingPoints.Value
                );
                if (response == null)
                    return;

                Server.NextWorldUpdate(() =>
                {
                    if (
                        !player.IsValid
                        || !_rankPlayers.TryGetValue(steamId, out var current)
                        || !ReferenceEquals(current, state)
                    )
                        return;

                    state.ServerPoints = response.Points;
                    state.IsLoaded = true;
                    ApplyRankScoreboard(player, state.Points);
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed loading rank state for {SteamId}", steamId);
            }
        });
    }

    private void HandleRankPlayerSpawn(CCSPlayerController player)
    {
        if (!RanksActive || !IsRankPlayer(player))
            return;

        if (_rankPlayers.TryGetValue(player.SteamID, out var state) && state.IsLoaded)
        {
            Server.NextWorldUpdate(() =>
            {
                if (player.IsValid)
                    ApplyRankScoreboard(player, state.Points);
            });
        }
    }

    private void HandleRankPlayerDisconnect(CCSPlayerController player)
    {
        if (player.IsBot)
            return;

        var steamId = player.SteamID;
        if (steamId == 0)
            return;

        _playersWithNadeTrail.TryRemove(steamId, out _);
        _playersWithPlayerTrail.TryRemove(steamId, out _);
        _playerTrailLastOrigin.Remove(steamId);

        if (_rankPlayers.TryGetValue(steamId, out var state))
            FlushRankPlayerPoints(state);
        _rankPlayers.TryRemove(steamId, out _);
    }

    private void HandleRankEntityCreated(CEntityInstance entity)
    {
        if (!RanksActive || !entity.DesignerName.EndsWith("_projectile", StringComparison.Ordinal))
            return;

        TryAttachNadeTrail(entity, attempt: 0);
    }

    private void HandleRankEntityDeleted(CEntityInstance entity)
        => RemoveNadeTrail(entity.Index);

    private void HandleRankPlayerDeath(EventPlayerDeath @event)
    {
        if (!RankPointsAllowed())
            return;

        var victim = @event.Userid;
        var attacker = @event.Attacker;

        if (victim is null || !victim.IsValid)
            return;

        var suicide = attacker is null || !attacker.IsValid || attacker.Slot == victim.Slot;
        var teamKill =
            !suicide
            && attacker is not null
            && attacker.TeamNum == victim.TeamNum
            && attacker.TeamNum is (byte)CsTeam.Terrorist or (byte)CsTeam.CounterTerrorist;

        if (!suicide && !teamKill && attacker is not null && IsRankEligibleCombatant(attacker))
        {
            if (IsRankPlayer(attacker) && (ConVars.RankPointsForBots.Value || IsRankPlayer(victim)))
                ApplyRankKillRewards(attacker, @event);
        }

        if (IsRankPlayer(victim) && ConVars.RankDeathPoints.Value != 0 && !teamKill)
        {
            var deathCounts =
                suicide
                || (attacker is not null && IsRankPlayer(attacker))
                || (ConVars.RankPointsForBots.Value && attacker is not null && attacker.IsBot);

            if (deathCounts)
                AwardRankPoints(victim, ConVars.RankDeathPoints.Value, suicide ? "suicide" : "death");
        }
    }

    private void HandleRankRoundEnd(EventRoundEnd @event)
    {
        if (!RankPointsAllowed())
            return;

        var winner = (CsTeam)@event.Winner;
        if (winner is not (CsTeam.Terrorist or CsTeam.CounterTerrorist))
            return;

        foreach (var player in Utilities.GetPlayers().Where(IsRankPlayer))
        {
            if (player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist))
                continue;

            if (player.Team == winner)
                AwardRankPoints(player, ConVars.RankRoundWinPoints.Value, "round win");
            else if (ConVars.RankRoundLossPoints.Value != 0)
                AwardRankPoints(player, ConVars.RankRoundLossPoints.Value, "round loss");
        }

        FlushDirtyRankPoints();
    }

    [ConsoleCommand("css_rank", "Show your points rank")]
    [ConsoleCommand("css_points", "Show your points rank")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnRankPointsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || !IsRankPlayer(player) || !RanksActive)
            return;

        var steamId = player.SteamID;
        var userId = steamId.ToString();
        var name = player.PlayerName;
        _ = Task.Run(async () =>
        {
            try
            {
                var response = await Api.FetchPointsStateAsync(
                    userId,
                    name,
                    ConVars.RankStartingPoints.Value
                );
                Server.NextWorldUpdate(() =>
                {
                    if (!player.IsValid)
                        return;

                    if (response != null && _rankPlayers.TryGetValue(steamId, out var state))
                    {
                        state.ServerPoints = response.Points;
                        state.IsLoaded = true;
                        ApplyRankScoreboard(player, state.Points);
                    }

                    NotifyRankPlayer(
                        player,
                        "Your Rank",
                        response?.Rank is int position
                            ? $"You are ranked {{green}}#{position}{{default}}."
                            : "Your rank could not be found."
                    );
                });
            }
            catch (Exception ex)
            {
                Server.NextWorldUpdate(() =>
                {
                    Logger.LogError(ex, "Failed loading rank for {SteamId}", steamId);
                    if (player.IsValid)
                        NotifyPlayer(player, "Rank", "Failed to load your rank.");
                });
            }
        });
    }

    [ConsoleCommand("css_top", "Show the points leaderboard")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnTopCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || !IsRankPlayer(player) || !RanksActive)
            return;

        var viewerSteamId = player.SteamID;
        var userId = viewerSteamId.ToString();
        _ = Task.Run(async () =>
        {
            try
            {
                async Task<HexiumLeaderboardPage> LoadPage(int pageNumber)
                {
                    var result = await Api.FetchPointsLeaderboardAsync(
                        userId,
                        pageNumber,
                        RankLeaderboardPageSize
                    );
                    if (result == null)
                        return new HexiumLeaderboardPage([], 0, 1, 0, null);
                    return new HexiumLeaderboardPage(
                        result.Entries
                            .Select(entry => new HexiumLeaderboardEntry(
                                ulong.TryParse(entry.SteamId, out var id) ? id : 0,
                                entry.Name,
                                entry.Points,
                                entry.Rank))
                            .ToList(),
                        result.Page,
                        result.TotalPages,
                        result.TotalEntries,
                        result.ViewerRank);
                }

                var page = await LoadPage(0);

                Server.NextWorldUpdate(() =>
                {
                    if (!player.IsValid)
                        return;

                    if (!HexiumUI.TryShowLeaderboard(player, page, viewerSteamId, LoadPage))
                        NotifyPlayer(
                            player,
                            "Leaderboard",
                            "The leaderboard UI is unavailable. Check that HexiumCS-UI and the Panorama addon are loaded."
                        );
                });
            }
            catch (Exception ex)
            {
                Server.NextWorldUpdate(() =>
                {
                    Logger.LogError(ex, "Failed loading points leaderboard.");
                    if (player.IsValid)
                        NotifyPlayer(player, "Leaderboard", "Failed to load the points leaderboard.");
                });
            }
        });
    }

    /// <summary>
    /// Applies equipped trail colors from the InventorySim equipped v5 payload.
    /// Called whenever a player's equipped inventory is (re)fetched.
    /// </summary>
    internal void ApplyRankTrailsFromEquipped(ulong steamId, EquippedTrailsResponse? trails)
    {
        if (!RanksActive)
            return;

        SetRankNadeTrails(steamId, trails?.Nade, trails?.NadeByType);
        SetRankTrailEquipped(_playersWithPlayerTrail, steamId, trails?.Player);

        if (!_playersWithPlayerTrail.ContainsKey(steamId))
            _playerTrailLastOrigin.Remove(steamId);
    }

    private void SetRankNadeTrails(
        ulong steamId,
        Dictionary<byte, string>? nade,
        Dictionary<byte, Dictionary<string, string>>? nadeByType
    )
    {
        var result = new RankTrailColors
        {
            Terrorist = ParseRankTrailColor(nade, (byte)CsTeam.Terrorist),
            CounterTerrorist = ParseRankTrailColor(nade, (byte)CsTeam.CounterTerrorist),
            TerroristByType = ParseTrailColorsByType(
                nadeByType,
                (byte)CsTeam.Terrorist
            ),
            CounterTerroristByType = ParseTrailColorsByType(
                nadeByType,
                (byte)CsTeam.CounterTerrorist
            ),
        };

        if (
            result.Terrorist is null
            && result.CounterTerrorist is null
            && result.TerroristByType is null
            && result.CounterTerroristByType is null
        )
        {
            _playersWithNadeTrail.TryRemove(steamId, out _);
            return;
        }

        _playersWithNadeTrail[steamId] = result;
    }

    private static Dictionary<string, Color>? ParseTrailColorsByType(
        Dictionary<byte, Dictionary<string, string>>? colors,
        byte team
    )
    {
        if (
            colors is null
            || !colors.TryGetValue(team, out var entries)
            || entries is null
        )
            return null;

        var result = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        foreach (var (modelKey, colorHex) in entries)
        {
            if (string.IsNullOrWhiteSpace(modelKey) || string.IsNullOrWhiteSpace(colorHex))
                continue;
            var parsed = ColorTranslator.FromHtml(colorHex.Trim());
            result[modelKey] = Color.FromArgb(255, parsed.R, parsed.G, parsed.B);
        }
        return result.Count > 0 ? result : null;
    }

    private static void SetRankTrailEquipped(
        ConcurrentDictionary<ulong, RankTrailColors> map,
        ulong steamId,
        Dictionary<byte, string>? colors
    )
    {
        var result = new RankTrailColors
        {
            Terrorist = ParseRankTrailColor(colors, (byte)CsTeam.Terrorist),
            CounterTerrorist = ParseRankTrailColor(colors, (byte)CsTeam.CounterTerrorist),
        };

        if (result.Terrorist is null && result.CounterTerrorist is null)
        {
            map.TryRemove(steamId, out _);
            return;
        }

        map[steamId] = result;
    }

    private static Color? ParseRankTrailColor(Dictionary<byte, string>? colors, byte team)
    {
        if (
            colors is null
            || !colors.TryGetValue(team, out var colorHex)
            || string.IsNullOrWhiteSpace(colorHex)
        )
            return null;

        var parsed = ColorTranslator.FromHtml(colorHex.Trim());
        return Color.FromArgb(255, parsed.R, parsed.G, parsed.B);
    }

    private void TryAttachNadeTrail(CEntityInstance entity, int attempt)
    {
        Server.NextWorldUpdate(() =>
        {
            if (!entity.IsValid)
                return;

            if (!RanksActive)
                return;

            var grenade = entity.As<CBaseCSGrenadeProjectile>();
            if (!grenade.IsValid)
                return;

            if (_nadeTrailTrackers.ContainsKey(grenade.Index))
                return;

            var thrower = ResolveGrenadeThrower(grenade);
            if (thrower is null)
            {
                if (attempt + 1 < NadeTrailAttachMaxAttempts)
                    TryAttachNadeTrail(entity, attempt + 1);
                else
                    Logger.LogDebug("Grenade projectile {EntityIndex} never got a valid thrower.", grenade.Index);
                return;
            }

            var steamId = thrower.SteamID != 0
                ? thrower.SteamID
                : thrower.AuthorizedSteamID?.SteamId64 ?? 0;
            if (
                steamId == 0
                || !_playersWithNadeTrail.TryGetValue(steamId, out var trailColors)
            )
                return;

            var trailColor = trailColors.ForGrenade(
                thrower.TeamNum,
                grenade.DesignerName
            );
            if (trailColor == null)
                return;

            var origin = grenade.AbsOrigin;
            if (origin is null)
            {
                if (attempt + 1 < NadeTrailAttachMaxAttempts)
                    TryAttachNadeTrail(entity, attempt + 1);
                return;
            }

            _nadeTrailTrackers[grenade.Index] = new NadeTrailTracker(
                grenade,
                trailColor.Value,
                new Vector(origin.X, origin.Y, origin.Z));

            Logger.LogDebug(
                "Tracking nade trail ({Color}) on grenade {GrenadeIndex} for {SteamId}.",
                trailColor.Value,
                grenade.Index,
                steamId
            );
        });
    }

    private static CCSPlayerController? ResolveGrenadeThrower(CBaseCSGrenadeProjectile grenade)
    {
        var throwerPawn = grenade.Thrower.Value;
        if (throwerPawn is { IsValid: true })
        {
            if (throwerPawn.Controller.Value is CCSPlayerController controller && controller.IsValid)
                return controller;
            if (throwerPawn.OriginalController.Value is { IsValid: true } original)
                return original;
        }

        if (grenade.OwnerEntity.Value is CCSPlayerController ownerController && ownerController.IsValid)
            return ownerController;

        if (grenade.OwnerEntity.Value is CCSPlayerPawn ownerPawn &&
            ownerPawn.Controller.Value is CCSPlayerController ownedBy &&
            ownedBy.IsValid)
            return ownedBy;

        return null;
    }

    private void UpdateNadeTrailBeams()
    {
        if (_nadeTrailTrackers.Count == 0)
            return;

        List<uint>? expired = null;
        foreach (var (index, tracker) in _nadeTrailTrackers)
        {
            var grenade = tracker.Grenade;
            if (!grenade.IsValid || grenade.AbsOrigin is null)
            {
                expired ??= [];
                expired.Add(index);
                continue;
            }

            var origin = grenade.AbsOrigin;
            var last = tracker.LastOrigin;
            var dx = origin.X - last.X;
            var dy = origin.Y - last.Y;
            var dz = origin.Z - last.Z;
            if ((dx * dx) + (dy * dy) + (dz * dz) < TrailMinSegmentDistance * TrailMinSegmentDistance)
                continue;

            CreateRankTrailBeam(origin, last, tracker.Color, TrailKind.Nade);
            last.X = origin.X;
            last.Y = origin.Y;
            last.Z = origin.Z;
        }

        if (expired is null)
            return;

        foreach (var index in expired)
            RemoveNadeTrail(index);
    }

    private void UpdateRankPlayerTrails()
    {
        if (_playersWithPlayerTrail.IsEmpty)
            return;

        foreach (var player in Utilities.GetPlayers())
        {
            if (!IsRankPlayer(player))
                continue;

            var steamId = player.SteamID;
            if (
                steamId == 0
                || !_playersWithPlayerTrail.TryGetValue(steamId, out var trailColors)
            )
                continue;

            var trailColor = trailColors.ForTeam(player.TeamNum);
            if (trailColor == null)
            {
                _playerTrailLastOrigin.Remove(steamId);
                continue;
            }

            var pawn = player.PlayerPawn.Value;
            if (pawn is null || !pawn.IsValid || pawn.AbsOrigin is null)
                continue;
            if (pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
            {
                _playerTrailLastOrigin.Remove(steamId);
                continue;
            }

            var origin = pawn.AbsOrigin;
            if (!_playerTrailLastOrigin.TryGetValue(steamId, out var last))
            {
                _playerTrailLastOrigin[steamId] = new Vector(origin.X, origin.Y, origin.Z);
                continue;
            }

            var dx = origin.X - last.X;
            var dy = origin.Y - last.Y;
            var dz = origin.Z - last.Z;
            if ((dx * dx) + (dy * dy) + (dz * dz) < TrailMinSegmentDistance * TrailMinSegmentDistance)
                continue;

            CreateRankTrailBeam(origin, last, trailColor.Value, TrailKind.Player);
            last.X = origin.X;
            last.Y = origin.Y;
            last.Z = origin.Z;
        }
    }

    private void CreateRankTrailBeam(Vector start, Vector end, Color color, TrailKind kind)
    {
        var beam = Utilities.CreateEntityByName<CBeam>("env_beam");
        if (beam is null || !beam.IsValid)
            return;

        beam.RenderMode = RenderMode_t.kRenderTransAlpha;
        beam.Width = TrailBeamWidth;
        beam.EndWidth = TrailBeamWidth;
        beam.Render = color;
        beam.Teleport(start, QAngle.Zero, Vector.Zero);
        beam.EndPos.X = end.X;
        beam.EndPos.Y = end.Y;
        beam.EndPos.Z = end.Z;
        Utilities.SetStateChanged(beam, "CBeam", "m_vecEndPos");

        var beamSet = kind == TrailKind.Nade ? _nadeTrailBeams : _playerTrailBeams;
        beamSet.Add(beam);

        AddTimer(TrailBeamLifetime, () =>
        {
            beamSet.Remove(beam);
            if (beam.IsValid)
                beam.Remove();
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void OnRanksCheckTransmit(CCheckTransmitInfoList infoList)
    {
        var hideNade = TrailViewPersistence.HasNadeOptOuts && _nadeTrailBeams.Count > 0;
        var hidePlayer = TrailViewPersistence.HasPlayerOptOuts && _playerTrailBeams.Count > 0;
        if (!hideNade && !hidePlayer)
            return;

        foreach (var (info, player) in infoList)
        {
            if (player is null || !player.IsValid || player.IsBot)
                continue;

            var steamId = player.SteamID;
            if (steamId == 0)
                continue;

            if (hideNade && TrailViewPersistence.IsNadeOptedOut(steamId))
            {
                foreach (var beam in _nadeTrailBeams)
                {
                    if (beam.IsValid)
                        info.TransmitEntities.Remove(beam);
                }
            }

            if (hidePlayer && TrailViewPersistence.IsPlayerOptedOut(steamId))
            {
                foreach (var beam in _playerTrailBeams)
                {
                    if (beam.IsValid)
                        info.TransmitEntities.Remove(beam);
                }
            }
        }
    }

    private void RemoveNadeTrail(uint grenadeIndex)
        => _nadeTrailTrackers.Remove(grenadeIndex);

    /// <summary>
    /// Removes active trail beams while they are still transmitted, so clients do not keep
    /// ghost segments after CheckTransmit starts hiding them.
    /// </summary>
    private void PurgeActiveTrailBeams(TrailKind kind)
    {
        var beamSet = kind == TrailKind.Nade ? _nadeTrailBeams : _playerTrailBeams;
        foreach (var beam in beamSet)
        {
            if (beam.IsValid)
                beam.Remove();
        }
        beamSet.Clear();
    }

    private void ClearRankTrails()
    {
        _nadeTrailTrackers.Clear();
        _nadeTrailBeams.Clear();
        _playerTrailBeams.Clear();
    }

    private sealed class RankTrailColors
    {
        public Color? Terrorist { get; init; }
        public Color? CounterTerrorist { get; init; }
        public Dictionary<string, Color>? TerroristByType { get; init; }
        public Dictionary<string, Color>? CounterTerroristByType { get; init; }

        public Color? ForTeam(byte team)
        {
            if (team == (byte)CsTeam.Terrorist)
                return Terrorist;
            if (team == (byte)CsTeam.CounterTerrorist)
                return CounterTerrorist;
            return null;
        }

        public Color? ForGrenade(byte team, string designerName)
        {
            var byType = team == (byte)CsTeam.Terrorist
                ? TerroristByType
                : team == (byte)CsTeam.CounterTerrorist
                    ? CounterTerroristByType
                    : null;
            if (byType is not null)
            {
                var modelKey = designerName.EndsWith("_projectile", StringComparison.Ordinal)
                    ? designerName[..^"_projectile".Length]
                    : designerName;
                if (byType.TryGetValue(modelKey, out var color))
                    return color;
            }
            return ForTeam(team);
        }
    }

    private sealed class NadeTrailTracker(
        CBaseCSGrenadeProjectile grenade,
        Color color,
        Vector lastOrigin)
    {
        public CBaseCSGrenadeProjectile Grenade { get; } = grenade;
        public Color Color { get; } = color;
        public Vector LastOrigin { get; } = lastOrigin;
    }

    private void ApplyRankKillRewards(CCSPlayerController attacker, EventPlayerDeath @event)
    {
        var parts = new List<(int Delta, string Reason)>(8);

        void Queue(int delta, string reason)
        {
            if (delta != 0)
                parts.Add((delta, reason));
        }

        Queue(ConVars.RankKillPoints.Value, "kill");

        if (@event.Headshot)
            Queue(ConVars.RankHeadshotPoints.Value, "headshot");

        if (@event.Noscope)
            Queue(ConVars.RankNoscopePoints.Value, "noscope");

        if (@event.Thrusmoke)
            Queue(ConVars.RankSmokeKillPoints.Value, "smoke kill");

        if (@event.Penetrated > 0)
            Queue(ConVars.RankWallbangPoints.Value, "wallbang");

        var weapon = (@event.Weapon ?? string.Empty).ToLowerInvariant();
        if (weapon.Contains("knife") || weapon.Contains("bayonet"))
            Queue(ConVars.RankKnifeKillPoints.Value, "knife kill");
        else if (weapon is "taser" or "zeus")
            Queue(ConVars.RankZeusKillPoints.Value, "zeus kill");

        AwardRankPointsBatch(attacker, parts);
    }

    private void AwardRankPoints(CCSPlayerController player, int delta, string reason)
    {
        if (delta == 0)
            return;

        AwardRankPointsBatch(player, [(delta, reason)]);
    }

    private void AwardRankPointsBatch(CCSPlayerController player, List<(int Delta, string Reason)> parts)
    {
        if (parts.Count == 0 || !RanksActive || !IsRankPlayer(player))
            return;

        if (!_rankPlayers.TryGetValue(player.SteamID, out var state) || !state.IsLoaded)
            return;

        var totalDelta = 0;
        foreach (var (delta, _) in parts)
        {
            totalDelta += delta;
            Interlocked.Add(ref state.PendingDelta, delta);
        }

        var points = state.Points;
        ApplyRankScoreboard(player, points);

        if (!ConVars.RankShowPointMessages.Value || totalDelta == 0)
            return;

        NotifyRankPlayer(player, "Points", FormatRankPointsMessage(parts, points));
    }

    private static string FormatRankPointsMessage(List<(int Delta, string Reason)> parts, int points)
    {
        if (parts.Count == 1)
        {
            var (delta, reason) = parts[0];
            var sign = delta > 0 ? $"+{delta}" : delta.ToString();
            var color = delta > 0 ? "{green}" : "{lightred}";
            return $"{color}{sign}{{default}} points for {reason} ({{green}}{points}{{default}}).";
        }

        var total = parts.Sum(p => p.Delta);
        var totalSign = total > 0 ? $"+{total}" : total.ToString();
        var totalColor = total > 0 ? "{green}" : "{lightred}";

        var breakdown = string.Join(
            ", ",
            parts.Select(p =>
            {
                var sign = p.Delta > 0 ? $"+{p.Delta}" : p.Delta.ToString();
                var color = p.Delta > 0 ? "{green}" : "{lightred}";
                return $"{color}{sign}{{default}} {p.Reason}";
            }));

        return $"{totalColor}{totalSign}{{default}} ({breakdown}) ({{green}}{points}{{default}}).";
    }

    private void NotifyRankPlayer(CCSPlayerController player, string title, string message, int duration = 4)
        => NotifyPlayer(player, title, message, duration);

    private void ApplyRankScoreboard(CCSPlayerController player, int points)
    {
        if (!ConVars.RankShowPremierOnScoreboard.Value || !player.IsValid)
            return;

        player.CompetitiveWins = 10;
        player.CompetitiveRankType = PremierRankType;
        player.CompetitiveRanking = Math.Max(0, points);
        Utilities.SetStateChanged(player, "CCSPlayerController", "m_iCompetitiveRankType");
    }

    private void FlushDirtyRankPoints()
    {
        foreach (var (_, state) in _rankPlayers)
            FlushRankPlayerPoints(state);
    }

    private void FlushAllRankPoints()
    {
        foreach (var (_, state) in _rankPlayers)
            FlushRankPlayerPoints(state);
    }

    private void FlushRankPlayerPoints(RankPlayerState state)
    {
        var delta = Interlocked.Exchange(ref state.PendingDelta, 0);
        if (delta == 0)
            return;

        var userId = state.SteamId.ToString();
        var name = state.Name;
        _ = Task.Run(async () =>
        {
            try
            {
                var points = await Api.AdjustPointsAsync(userId, delta, name);
                if (points.HasValue)
                    Interlocked.Add(ref state.ServerPoints, delta);
                else
                    Interlocked.Add(ref state.PendingDelta, delta);
            }
            catch (Exception ex)
            {
                Interlocked.Add(ref state.PendingDelta, delta);
                Logger.LogError(ex, "Failed flushing rank points for {SteamId}", state.SteamId);
            }
        });
    }

    private bool RankPointsAllowed()
    {
        if (!RanksActive)
            return false;

        if (!ConVars.RankPointsDuringWarmup.Value && IsRankWarmup())
            return false;

        var humans = Utilities.GetPlayers().Count(IsRankPlayer);
        return humans >= Math.Max(0, ConVars.RankMinPlayers.Value);
    }

    private static bool IsRankWarmup()
    {
        var gameRules = Utilities
            .FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
            .FirstOrDefault()?.GameRules;
        return gameRules?.WarmupPeriod ?? false;
    }

    private static bool IsRankPlayer(CCSPlayerController? player)
        => player is not null
           && player.IsValid
           && !player.IsBot
           && !player.IsHLTV
           && player.SteamID != 0;

    private static bool IsRankEligibleCombatant(CCSPlayerController? player)
        => player is not null && player.IsValid && !player.IsHLTV;

    [ConsoleCommand("css_givepoints", "Give points to a player by name or #userid")]
    [CommandHelper(minArgs: 2, usage: "<target> <amount>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    [RequiresPermissions("@css/root")]
    public void OnGivePointsCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!RanksActive)
        {
            command.ReplyToCommand("Ranks are not enabled or the API key is missing.");
            return;
        }

        if (!int.TryParse(command.GetArg(2), out var amount) || amount == 0)
        {
            command.ReplyToCommand("Amount must be a non-zero integer.");
            return;
        }

        var targetName = command.GetArg(1);
        var targets = Utilities.GetPlayers()
            .Where(IsRankPlayer)
            .Where(p =>
                p.PlayerName.Contains(targetName, StringComparison.OrdinalIgnoreCase)
                || $"#{p.UserId}".Equals(targetName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (targets.Count == 0)
        {
            command.ReplyToCommand($"No player matched '{targetName}'.");
            return;
        }

        if (targets.Count > 1)
        {
            command.ReplyToCommand($"Multiple players matched '{targetName}'.");
            return;
        }

        var target = targets[0];
        var steamId = target.SteamID;
        var name = target.PlayerName;
        var userId = steamId.ToString();

        _ = Task.Run(async () =>
        {
            try
            {
                var points = await Api.AdjustPointsAsync(userId, amount, name);
                if (!points.HasValue)
                {
                    Server.NextWorldUpdate(() =>
                    {
                        command.ReplyToCommand("Failed to save points to the database.");
                    });
                    return;
                }

                Server.NextWorldUpdate(() =>
                {
                    if (_rankPlayers.TryGetValue(steamId, out var state))
                    {
                        state.ServerPoints = points.Value;
                        ApplyRankScoreboard(target, state.Points);
                    }

                    var sign = amount > 0 ? $"+{amount}" : amount.ToString();
                    if (target.IsValid)
                    {
                        NotifyRankPlayer(
                            target,
                            "Points Updated",
                            $"An admin adjusted your points by {{green}}{sign}{{default}}. Now {{green}}{state?.Points ?? points.Value}{{default}}."
                        );
                    }

                    command.ReplyToCommand($"Set {name} to {points.Value} points ({sign}).");
                });
            }
            catch (Exception ex)
            {
                Server.NextWorldUpdate(() =>
                {
                    Logger.LogError(ex, "Failed giving points to {SteamId}", steamId);
                    command.ReplyToCommand("Failed to save points to the database.");
                });
            }
        });
    }

    [ConsoleCommand("css_rankpanorama", "Toggle your Panorama point notifications (!rankpanorama [0|1])")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnRankPanoramaCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || !IsRankPlayer(player))
            return;

        var steamId = player.AuthorizedSteamID?.SteamId64;
        if (steamId is null)
            return;

        var currentlyEnabled = ConVars.UsePanoramaUI.Value && !_panoramaOptOut.Contains(steamId.Value);
        var arg = command.GetArg(1);
        bool enabled;
        if (string.IsNullOrWhiteSpace(arg))
        {
            enabled = !currentlyEnabled;
        }
        else
        {
            switch (arg.Trim().ToLowerInvariant())
            {
                case "1":
                case "on":
                case "true":
                case "enable":
                case "enabled":
                    enabled = true;
                    break;
                case "0":
                case "off":
                case "false":
                case "disable":
                case "disabled":
                    enabled = false;
                    break;
                default:
                    command.ReplyToCommand("Usage: !rankpanorama [0|1]");
                    return;
            }
        }

        if (enabled)
            _panoramaOptOut.Remove(steamId.Value);
        else
            _panoramaOptOut.Add(steamId.Value);

        if (enabled && !ConVars.UsePanoramaUI.Value)
        {
            player.PrintToChat(
                $" {ReplaceColorTags(Localizer["invsim.panorama_disabled_server", Rules.GetChatPrefix()].ToString())}"
            );
            return;
        }

        NotifyPlayer(
            player,
            "Inventory",
            Localizer[enabled ? "invsim.panorama_enabled" : "invsim.panorama_disabled", ""].ToString()
        );
    }
}
