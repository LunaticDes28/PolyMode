using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using Polytopia.Data;
using PolytopiaBackendBase.Common;
using UnityEngine.EventSystems;
using PolytopiaBackendBase.Game;

namespace Polyquest
{
    public static class Main
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameRules), nameof(GameRules.LoadPreset))]
        private static void GameRules_LoadPreset_Postfix(GameRules __instance, GameMode gameMode)
        {
            if (gameMode == EnumCache<GameMode>.GetType("conquest") || (int)gameMode == 8)
            {
                __instance.AllowMirrorPick = false;
                __instance.AllowTechSharing = false;
                __instance.AllowSpecialTribes = true;
                __instance.ScoreLimit = 0;
                __instance.TurnLimit = 0;
                __instance.WinByCapital = false;
                __instance.WinByExtermination = true;
                __instance.PlayerDeathCondition = GameRules.DeathCondition.Cities;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateInternal))]
        private static void GenerateInternal_Postfix(MapGenerator __instance, GameState gameState, MapGeneratorSettings settings)
        {
            bool isConquest = gameState.Settings.RulesGameMode == EnumCache<GameMode>.GetType("conquest") || 
                              gameState.Settings.BaseGameMode == EnumCache<GameMode>.GetType("conquest");

            if (!isConquest) return;

            Loader.modLogger!.LogInfo("[Conquest] Initializing vanilla Manhattan-proximity equal village distribution...");
            DistributeProximityVillages(__instance, gameState.Map, gameState);
        }

        private static void DistributeProximityVillages(MapGenerator gen, MapData map, GameState state)
        {
            int playerCount = state.PlayerCount;
            if (playerCount == 0) return;

            // 1. Gather existing unowned neutral villages
            List<TileData> neutralVillages = new List<TileData>();
            for (int i = 0; i < map.Tiles.Length; i++)
            {
                TileData tile = map.Tiles[i];
                if (tile.HasImprovement(ImprovementData.Type.City) && tile.owner == 0)
                {
                    neutralVillages.Add(tile);
                }
            }

            int remainder = neutralVillages.Count % playerCount;

            // 2. Threshold Rule: Spawn emergency villages if close to another full tier
            if (remainder > 0 && remainder >= (playerCount * 0.6f))
            {
                int citiesToSpawn = playerCount - remainder;
                Loader.modLogger!.LogInfo($"[Conquest] Spawning {citiesToSpawn} emergency villages to complete tier.");

                for (int s = 0; s < citiesToSpawn; s++)
                {
                    WorldCoordinates emergencyCoords = gen.GetEmergencyCityPosition(state, map);
                    if (emergencyCoords != WorldCoordinates.NULL_COORDINATES)
                    {
                        int tileIndex = map.GetTileIndex(emergencyCoords);
                        TileData targetTile = map.Tiles[tileIndex];
                        targetTile.improvement = new ImprovementState
                        {
                            type = ImprovementData.Type.City,
                            founded = 0,
                            level = 1,
                            borderSize = 1,
                            production = 1
                        };
                        neutralVillages.Add(targetTile);
                    }
                    else
                    {
                        break;
                    }
                }
            }

            int totalVillages = neutralVillages.Count;
            int maxCitiesPerPlayer = totalVillages / playerCount;

            Loader.modLogger!.LogInfo($"[Conquest] Total pool: {totalVillages} villages. Allocating {maxCitiesPerPlayer} closest villages per player.");

            HashSet<WorldCoordinates> assignedCoordinates = new HashSet<WorldCoordinates>();

            // 3. Tiered Handout Loop using the native grid system calculation
            for (int round = 0; round < maxCitiesPerPlayer; round++)
            {
                for (int p = 0; p < playerCount; p++)
                {
                    PlayerState player = state.PlayerStates[p];
                    WorldCoordinates capitalCoords = player.startTile;

                    TileData closestVillage = null;
                    int closestDistance = int.MaxValue;

                    foreach (var village in neutralVillages)
                    {
                        if (assignedCoordinates.Contains(village.coordinates)) 
                            continue;

                        // INTEGRATED VANILLA METHOD: Use the exact Manhattan distance calculation
                        int distance = MapDataExtensions.ManhattanDistance(capitalCoords, village.coordinates);
                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                            closestVillage = village;
                        }
                    }

                    if (closestVillage != null)
                    {
                        assignedCoordinates.Add(closestVillage.coordinates);
                        InitializeConquestCityData(state, closestVillage, player);
                    }
                }
            }

            // 4. Convert leftover unassigned outer frontier villages into neutral Ruins
            int ruinsCount = 0;
            foreach (var village in neutralVillages)
            {
                if (!assignedCoordinates.Contains(village.coordinates))
                {
                    village.improvement = new ImprovementState
                    {
                        type = ImprovementData.Type.Ruin,
                        borderSize = 0,
                        level = 1,
                        production = 1,
                        founded = 0
                    };
                    village.owner = 0;
                    ruinsCount++;
                }
            }

            Loader.modLogger!.LogInfo($"[Conquest] Proximity distribution done. Assigned {assignedCoordinates.Count} cities. Created {ruinsCount} ruins.");
        }

        private static void InitializeConquestCityData(GameState state, TileData tile, PlayerState player)
        {
            try
            {
                tile.owner = player.Id;

                TribeData tribeData;
                if (state.GameLogicData.TryGetData(player.tribe, out tribeData) && tribeData != null)
                {
                    string generatedName = MapDataExtensions.GenerateCityName(state, tile.coordinates, tribeData.language);
                    
                    if (tile.improvement != null)
                    {
                        tile.improvement.name = generatedName;
                    }
                }

                player.cities++;

                Il2CppSystem.Collections.Generic.List<TileData> cityArea = ActionUtils.GetCityAreaSorted(state, tile);
                for (int j = 0; j < cityArea.Count; j++)
                {
                    TileData territoryTile = cityArea[j];
                    territoryTile.owner = player.Id;
                    territoryTile.rulingCityCoordinates = tile.coordinates;
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger!.LogError($"[Conquest] Failed to safely assign proximity city parameters: {ex}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameLogicData), nameof(GameLogicData.GetTechPrice))]
        private static void Conquest_TechCost_Postfix(GameLogicData __instance, TechData techData, PlayerState playerState, GameState state, ref int __result)
        {
            if (state == null || techData == null) return;

            bool isConquest = state.Settings.RulesGameMode == EnumCache<GameMode>.GetType("conquest") || 
                            state.Settings.BaseGameMode == EnumCache<GameMode>.GetType("conquest");

            if (!isConquest) return;

            // Tech becomes more expensive over time in Conquest mode
            int addition = (int)state.CurrentTurn;   // +1 per turn
            addition = Math.Min(addition, techData.cost * 5);    // Cap at 5x

            __result = (int)Math.Ceiling((double)(__result + addition));
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CaptureCityAction), nameof(CaptureCityAction.ExecuteDefault))]   // Change if method name is different
        private static bool Conquest_CaptureCityAction_Prefix(CaptureCityAction __instance, GameState gameState)
        {
            if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest"))
            {
                return true;
            } 

		TileData cityTile = gameState.Map.GetTile(__instance.Coordinates);
		PlayerState playerState;
		gameState.TryGetPlayer(__instance.PlayerId, out playerState);
            DestroyCityConquest(gameState, cityTile, playerState);
            return false;
        }

        private static void DestroyCityConquest(GameState gameState, TileData cityTile, PlayerState attacker)
        {
            if (cityTile?.improvement?.type != ImprovementData.Type.City) return;

            // Reward
            int reward = cityTile.improvement.level * 2 + (int)gameState.CurrentTurn;
            attacker.Currency = attacker.Currency + reward;
            bool leaveRuin = UnityEngine.Random.value < 0.6f;

            // Leave ruin sometimes
            if (leaveRuin)
            {
                cityTile.improvement = new ImprovementState 
                { 
                    type = ImprovementData.Type.Ruin, 
                    level = 1 
                };
            }
            else
            {
                cityTile.improvement = null;
            }

            cityTile.owner = 0;
            cityTile.capitalOf = 0;

            Log.Info($"[Conquest] City destroyed by player {attacker.Id} (+{reward} stars)");
        }
    }
}