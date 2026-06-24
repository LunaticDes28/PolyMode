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
        /*[HarmonyPostfix]
        [HarmonyPatch(typeof(GameRules), nameof(GameRules.LoadPreset))]
        private static void GameRules_LoadPreset_Postfix(GameRules __instance, GameMode gameMode)
        {
                Loader.modLogger?.LogInfo("GameRules.LoadPreset");
                
            if (gameMode == EnumCache<GameMode>.GetType("conquest"))
            {   
                Loader.modLogger?.LogInfo("GameRules.LoadPreset.Conquest");
                __instance.AllowMirrorPick = false;
                __instance.AllowTechSharing = false;
                __instance.AllowSpecialTribes = true;
                __instance.ScoreLimit = 0;
                __instance.TurnLimit = 0;
                __instance.WinByCapital = false;
                __instance.WinByExtermination = true;
                __instance.PlayerDeathCondition = GameRules.DeathCondition.Cities;
            }
        }*/

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateInternal))]
        private static void GenerateInternal_Postfix(MapGenerator __instance, GameState gameState, MapGeneratorSettings settings)
        {
            // 1. Check your dictionary safely using your working logic
            bool isConquest = Loader.IsConquestMode(gameState);

            if (!isConquest) return;

            Loader.modLogger?.LogInfo("[Conquest-Map] Conquest signature verified via dictionary key! Distributing villages...");
            
            // 2. Run your beautiful Manhattan proximity code
            DistributeProximityVillages(__instance, gameState.Map, gameState);

            // ====================== THE PERMANENT FIX ======================
            // 3. Stamp a permanent signature into an unused match setting (like turn limit) 
            // so the game remembers this is a Conquest match even after saving and reloading!
            gameState.Settings.RulesGameMode = EnumCache<GameMode>.GetType("conquest");

            // 4. RESET THE DOMINATION DICTIONARY FLAG IMMEDIATELY!
            // This un-hijacks the normal Domination mode so players can play standard matches next time.
            Loader.SetConquestMode(gameState.Settings, false);
            Loader.modLogger?.LogInfo("[Conquest-Map] Map generated. Conquest flag safely reset to false.");
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
                        gen.SetTileAsCity(targetTile);
                        neutralVillages.Add(targetTile);
                        Loader.modLogger!.LogInfo($"[Conquest] Spawned {citiesToSpawn} emergency villages on tile {targetTile.coordinates}.");
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
                    Loader.modLogger!.LogInfo($"[Conquest] Identified player {player.Id} at {capitalCoords}.");

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
                            Loader.modLogger!.LogInfo($"[Conquest] Identified closest village with distance {distance} at {village.coordinates}.");
                        }
                    }

                    if (closestVillage != null)
                    {
                        assignedCoordinates.Add(closestVillage.coordinates);
                        InitializeConquestCityData(state, closestVillage, player);
                    }
                }
                Loader.modLogger!.LogInfo($"[Conquest] Finished round {round+1} Conquest City Distribution.");
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

            Loader.modLogger!.LogInfo($"[Conquest] Initialize Conquest City Data.");

                tile.owner = player.Id;

                TribeData tribeData;
                if (state.GameLogicData.TryGetData(player.tribe, out tribeData) && tribeData != null)
                {
                    // if (state.GameLogicData.TryGetData(player.skinType, out skin) && skin != null) return;
                    string generatedName = MapDataExtensions.GenerateCityName(state, tile.coordinates, tribeData, player.skinType);
                    
                    if (tile.improvement != null)
                    {
                        tile.improvement.name = generatedName;
                        Loader.modLogger!.LogInfo($"[Conquest] Initialize Conquest City Name: {generatedName}.");
                    }
                }

                player.cities++;

                Il2CppSystem.Collections.Generic.List<TileData> cityArea = ActionUtils.GetCityAreaSorted(state, tile);
                for (int j = 0; j < cityArea.Count; j++)
                {
                    TileData territoryTile = cityArea[j];
                    territoryTile.owner = player.Id;
                    territoryTile.rulingCityCoordinates = tile.coordinates;
                    Loader.modLogger!.LogInfo($"[Conquest] Initialize Conquest City Labelled: {player.Id} + {territoryTile.rulingCityCoordinates}.");
                }

                ActionUtils.RuleArea(state, player, tile, false);
			    ActionUtils.ExploreFromTile(state, player, tile, 2, false);
            }
            catch (Exception ex)
            {
                Loader.modLogger!.LogError($"[Conquest] Failed to safely assign proximity city parameters: {ex}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameManager), nameof(GameManager.OnLevelLoaded))] 
        private static void OnLevelLoaded_Postfix(GameManager __instance)
        {
            if (__instance == null || __instance.settings == null || __instance.settings.rules == null) return;

            // Since 9999 remains active in the file permanently, we re-hook our dictionary flag on every single reload
            /*if (__instance.settings.rules.TurnLimit == 9999)
            {
                Loader.modLogger?.LogInfo("[Conquest-Save] Conquest match save loaded! Re-enabling runtime flag rules...");
                Loader.SetConquestMode(__instance.settings, true);
            }*/
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GameManager), nameof(GameManager.OnLevelUnloaded))]
        private static void OnLevelUnloaded_Postfix(GameManager __instance)        
        {
            /*try
            {
                if (__instance != null && __instance.settings != null)
                {
                    Loader.modLogger?.LogInfo("[Conquest-Exit] Player is exiting the match. Forcing dictionary flag deactivation...");
                    
                    // Explicitly flip our dictionary flag off to protect the main menu state
                    Loader.SetConquestMode(__instance.settings, false);
                    
                    Loader.modLogger?.LogInfo("[Conquest-Exit] Flag cleared. Main menu state safely restored to vanilla defaults.");
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Exit] Error cleaning up match state flags on exit: {ex}");
            }*/
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameLogicData), nameof(GameLogicData.GetTechPrice))]
        private static void Conquest_TechCost_Postfix(GameLogicData __instance, TechData techData, PlayerState playerState, GameState state, ref int __result)
        {
            if (state == null || techData == null) return;

            bool isConquest = Loader.IsConquestMode(state);
            
            if (state.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")) return;

            // Tech becomes more expensive over time in Conquest mode
            int addition = (int)(4 + state.CurrentTurn);   // +1 per turn
            addition = Math.Min(addition, 20 + techData.cost * 5);    // Cap at 5x

            __result = (int)Math.Ceiling((double)(techData.cost + addition));
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CaptureCityAction), nameof(CaptureCityAction.ExecuteDefault))]   // Change if method name is different
        private static bool Conquest_CaptureCityAction_Prefix(CaptureCityAction __instance, GameState gameState)
        {
        bool isConquest = Loader.IsConquestMode(gameState);
            if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest"))
            {
                return true;
            } 

		TileData cityTile = gameState.Map.GetTile(__instance.Coordinates);
		PlayerState attacker;
		gameState.TryGetPlayer(__instance.PlayerId, out attacker);
            DestroyCityConquest(gameState, cityTile, attacker);
            return false;
        }

        private static void DestroyCityConquest(GameState gameState, TileData cityTile, PlayerState attacker)
        {
            if (cityTile?.improvement?.type != ImprovementData.Type.City) return;

            // Reward
            int reward = cityTile.improvement.level * 2;
            attacker.Currency = attacker.Currency + reward;
            bool leaveRuin = UnityEngine.Random.value < 0.5f;

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
                cityTile.improvement = new ImprovementState 
                { 
                    type = ImprovementData.Type.None
                };
            }

            cityTile.owner = 0;
            cityTile.capitalOf = 0;

            Log.Info($"[Conquest] City destroyed by player {attacker.Id} (+{reward} stars)");
        }
    }
}