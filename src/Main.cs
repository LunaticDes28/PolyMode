using HarmonyLib;
using PolytopiaBackendBase.Game;
using Polytopia.Data;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Polyquest
{
    public static class Main
    {
        // Cache for pre-calculated city allocations during map generation
        private static readonly Dictionary<byte, List<WorldCoordinates>> _allocatedConquestCities = new Dictionary<byte, List<WorldCoordinates>>();

        // =========================================================================
        // A. Map Generation - Dry Run Allocation
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateInternal))]
        private static void GenerateInternal_Postfix(MapGenerator __instance, GameState gameState, MapGeneratorSettings settings)
        {
            if (gameState?.Settings == null) return;

            try
            {
                bool isConquest = UI_2.IsConquestSelected;
                if (!isConquest) return;

                Loader.modLogger?.LogInfo("[Conquest-Map] Conquest mode active! Starting village allocation...");

                _allocatedConquestCities.Clear();

                CalculateProximityVillagesAllocation(__instance, gameState.Map, gameState);

                // Lock game mode ID
                int registeredConquestId = PolyMod.Registry.gameModesAutoidx - 1;
                gameState.Settings.RulesGameMode = (GameMode)registeredConquestId;

                UI_2.IsConquestSelected = false;

                Loader.modLogger?.LogInfo($"[Conquest-Map] Allocation cached. RulesGameMode locked to {registeredConquestId}");
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Map] Error in GenerateInternal_Postfix: {ex.Message}");
            }
        }

        // =========================================================================
        // B. Apply actual cities on first turn
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(StartTurnAction), "ExecuteDefault")]
        private static void StartTurnAction_ExecuteDefault_Postfix(StartTurnAction __instance, GameState gameState)
        {
            if (gameState?.Settings == null) return;

            try
            {
                int registeredConquestId = PolyMod.Registry.gameModesAutoidx - 1;
                if ((int)gameState.Settings.RulesGameMode != registeredConquestId) return;

                if (gameState.CurrentTurn != 0U) return; // Only run on first turn

                byte playerId = __instance.PlayerId;

                if (_allocatedConquestCities.TryGetValue(playerId, out List<WorldCoordinates> coordsList) && coordsList != null)
                {
                    Loader.modLogger?.LogInfo($"[Conquest-Turn] Applying {coordsList.Count} cities for player {playerId}");

                    PlayerState playerState;
                    gameState.TryGetPlayer(playerId, out playerState);
                    if (playerState == null) return;

                    foreach (WorldCoordinates coords in coordsList)
                    {
                        TileData tile = gameState.Map.GetTile(coords);
                        if (tile == null) continue;

                        tile.owner = playerId;
                        tile.capitalOf = 0;

                        // Generate city name
                        TribeData tribeData;
                        if (gameState.GameLogicData.TryGetData(playerState.tribe, out tribeData) && tribeData != null)
                        {
                            string name = MapDataExtensions.GenerateCityName(gameState, tile.coordinates, tribeData, playerState.skinType);
                            if (tile.improvement != null)
                                tile.improvement.name = name;
                        }

                        playerState.cities++;

                        // Apply territory
                        var area = ActionUtils.GetCityAreaSorted(gameState, tile);
                        foreach (var t in area)
                        {
                            if (t != null)
                            {
                                t.owner = playerId;
                                t.rulingCityCoordinates = tile.coordinates;
                            }
                        }

                        ActionUtils.RuleArea(gameState, playerState, tile, false);
                        ActionUtils.ExploreFromTile(gameState, playerState, tile, 2, false);
                    }

                    _allocatedConquestCities.Remove(playerId);
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Turn] Error in StartTurnAction: {ex.Message}");
            }
        }

        // =========================================================================
        // C. Pure Math Allocation (no tile modification)
        // =========================================================================
        private static void CalculateProximityVillagesAllocation(MapGenerator gen, MapData map, GameState state)
        {
            int playerCount = state.PlayerCount;
            if (playerCount == 0) return;

            List<TileData> neutralVillages = new List<TileData>();
            for (int i = 0; i < map.Tiles.Length; i++)
            {
                TileData tile = map.Tiles[i];
                if (tile.HasImprovement(ImprovementData.Type.City) && tile.owner == 0)
                    neutralVillages.Add(tile);
            }

            // Emergency cities
            int remainder = neutralVillages.Count % playerCount;
            if (remainder > 0 && remainder >= (playerCount * 0.6f))
            {
                int toSpawn = playerCount - remainder;
                for (int s = 0; s < toSpawn; s++)
                {
                    WorldCoordinates pos = gen.GetEmergencyCityPosition(state, map);
                    if (pos != WorldCoordinates.NULL_COORDINATES)
                    {
                        TileData tile = map.GetTile(pos);
                        if (tile != null)
                        {
                            gen.SetTileAsCity(tile);
                            neutralVillages.Add(tile);
                        }
                    }
                }
            }

            int total = neutralVillages.Count;
            int maxPerPlayer = total / playerCount;
            HashSet<WorldCoordinates> assigned = new HashSet<WorldCoordinates>();

            for (int round = 0; round < maxPerPlayer; round++)
            {
                for (int p = 0; p < playerCount; p++)
                {
                    PlayerState player = state.PlayerStates[p];
                    WorldCoordinates capital = player.startTile;

                    TileData closest = null;
                    int bestDist = int.MaxValue;

                    foreach (var village in neutralVillages)
                    {
                        if (assigned.Contains(village.coordinates)) continue;

                        int dist = MapDataExtensions.ManhattanDistance(capital, village.coordinates);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            closest = village;
                        }
                    }

                    if (closest != null)
                    {
                        assigned.Add(closest.coordinates);

                        if (!_allocatedConquestCities.ContainsKey(player.Id))
                            _allocatedConquestCities[player.Id] = new List<WorldCoordinates>();

                        _allocatedConquestCities[player.Id].Add(closest.coordinates);
                    }
                }
            }

            // Leftover villages → Ruins
            foreach (var village in neutralVillages)
            {
                if (!assigned.Contains(village.coordinates))
                {
                    village.improvement = new ImprovementState { type = ImprovementData.Type.Ruin, level = 1 };
                }
            }
        }

        // =========================================================================
        // D. Tech Cost
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameLogicData), nameof(GameLogicData.GetTechPrice))]
        private static void Conquest_TechCost_Postfix(GameLogicData __instance, TechData techData, PlayerState playerState, GameState state, ref int __result)
        {
            if (state == null || techData == null) return;

            try
            {
                int registeredConquestId = PolyMod.Registry.gameModesAutoidx - 1;
                if ((int)state.Settings.RulesGameMode != registeredConquestId) return;

                int addition = (int)(4 + state.CurrentTurn);
                addition = Math.Min(addition, 20 + techData.cost * 5);

                __result = (int)Math.Ceiling((double)(techData.cost + addition));
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Tech] Error: {ex.Message}");
            }
        }

        // =========================================================================
        // E. City Destruction
        // =========================================================================
        [HarmonyPrefix]
        [HarmonyPatch(typeof(CaptureCityAction), nameof(CaptureCityAction.ExecuteDefault))]
        private static bool Conquest_CaptureCityAction_Prefix(CaptureCityAction __instance, GameState gameState)
        {
            if (gameState?.Settings == null) return true;

            try
            {
                int registeredConquestId = PolyMod.Registry.gameModesAutoidx - 1;
                if ((int)gameState.Settings.RulesGameMode != registeredConquestId) return true;

                TileData cityTile = gameState.Map.GetTile(__instance.Coordinates);
                PlayerState attacker = null;
                gameState.TryGetPlayer(__instance.PlayerId, out attacker);

                if (cityTile != null && attacker != null)
                    DestroyCityConquest(gameState, cityTile, attacker);

                return false;
            }
            catch
            {
                return true;
            }
        }

        private static void DestroyCityConquest(GameState gameState, TileData cityTile, PlayerState attacker)
        {
            if (cityTile?.improvement?.type != ImprovementData.Type.City) return;

            int reward = cityTile.improvement.level * 2 + (int)gameState.CurrentTurn;

            if (attacker != null)
            {
                attacker.Currency += reward;
                Log.Info($"[Conquest] City destroyed by player {attacker.Id} (+{reward} stars)");
            }

            bool leaveRuin = UnityEngine.Random.value < 0.6f;

            if (leaveRuin)
                cityTile.improvement = new ImprovementState { type = ImprovementData.Type.Ruin, level = 1 };
            else
                cityTile.improvement = null;

            cityTile.owner = 0;
            cityTile.capitalOf = 0;
        }
    }
}