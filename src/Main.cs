using HarmonyLib;
using PolytopiaBackendBase.Game;
using Polytopia.Data;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace PolyMode
{
    public static class Main
    {
        // =========================================================================
        // A. GameMode Settings
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameStateUtils), nameof(GameStateUtils.GenerateMap))]
        private static void GenerateMap_SetGamemode(GameState gameState)
        {
            try
            {
                bool isConquest = UI_2.IsConquestSelected;
                bool isReign = UI_2.IsReignSelected;
                if (!isConquest && !isReign) return;

                Loader.modLogger?.LogInfo("[Conquest-Map] Conquest Mode selected!");

                // Pseudo GameSettings in GameState
                if (isConquest) 
                {
                    gameState.Settings.RulesGameMode = EnumCache<GameMode>.GetType("conquest");
                    gameState.Settings.rules.WinByExtermination = true;
                    
                    Loader.modLogger?.LogInfo($"[Conquest-Map] RulesGameMode stamped as ID: {(int)gameState.Settings.RulesGameMode}");

                    UI_2.IsConquestSelected = false;
                    Loader.modLogger?.LogInfo($"[Conquest-Map] Flag IsConquestSelected is set {UI_2.IsConquestSelected}");               
                } 
                else if (isReign)
                {
                    gameState.Settings.RulesGameMode = EnumCache<GameMode>.GetType("reign");
                    gameState.Settings.rules.WinByCapital = true;
                    
                    Loader.modLogger?.LogInfo($"[Conquest-Map] RulesGameMode stamped as ID: {(int)gameState.Settings.RulesGameMode}");

                    UI_2.IsReignSelected = false;
                    Loader.modLogger?.LogInfo($"[Conquest-Map] Flag IsReignSelected is set {UI_2.IsReignSelected}");
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Map] GameStateUtils error: {ex.Message}");
            }
        }

        // =========================================================================
        // B. Capital Generation Logics
        // =========================================================================
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GeneratePlayerCapitalPositions))]
        private static bool GeneratePlayerCapitalPositions_NewQuadrants(
            MapGenerator __instance,
            int width,
            int playerCount,
            ref Il2CppSystem.Collections.Generic.List<int> __result)
        {
            try
            {
                Loader.modLogger?.LogInfo($"[CapitalGenerator] Started");

                if (GameManager.PreliminaryGameSettings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && GameManager.PreliminaryGameSettings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return true;
                }

                Loader.modLogger?.LogInfo($"[CapitalGenerator] Clustered City Mod started. Players={playerCount}, Width={width}");

                int num = (playerCount <= 4) ? 2 : 4;

                int num2 = width / num;
                if (num2 < 3)
                {
                    Loader.modLogger?.LogError($"Domain size {num2} is too small for {playerCount} players");
                    return true;
                }

                int val = width - num2 * num;
                int num3 = num * num;

                List<int> list = new List<int>();
                for (int i = 0; i < num3; i++)
                {
                    int domainX = i % num;
                    int domainY = i / num;

                    if (num == 2)
                    {
                        // simple case all quadrants usable
                        list.Add(i);
                    }
                    else if (num == 4)
                    {
                        // must isEdge but never isCorner
                        bool isEdge = (domainX == 0 || domainX == 3 || domainY == 0 || domainY == 3);
                        bool isCorner = (domainX == 0 || domainX == 3) && (domainY == 0 || domainY == 3);

                        // selected quadrants: 2, 3, 5, 8, 9, 12, 14, 15
                        if (isEdge && !isCorner)
                        {
                            list.Add(i);
                        }
                    }
                }

                Il2CppStructArray<int> probabilities = new Il2CppStructArray<int>(width * width);

                for (int j = 1; j < num; j++)
                {
                    for (int k = 1; k < num; k++)
                    {
                        int num4 = Math.Min(val, Math.Max(1, Math.Min(val, k) - 1));
                        int num5 = Math.Min(val, Math.Max(1, Math.Min(val, j) - 1));
                        int num6 = k * num2 + num4;
                        int num7 = j * num2 + num5;

                        __instance.AddDistanceToProbabilityTable(probabilities, width, new WorldCoordinates(num6 - 1, num7 - 1), num2);
                    }
                }

                List<int> list2 = new List<int>(playerCount);

                for (int l = 0; l < playerCount; l++)
                {
                    if (list.Count == 0) break;

                    int index = __instance.random.Range(0, list.Count);
                    int index2 = list[index];
                    list.RemoveAt(index);

                    WorldCoordinates worldCoordinates = WorldCoordinates.FromIndex(index2, num);
                    int num8 = Math.Min(val, Math.Max(1, Math.Min(val, worldCoordinates.X) - 1));
                    int num9 = Math.Min(val, Math.Max(1, Math.Min(val, worldCoordinates.Y) - 1));
                    int num10 = worldCoordinates.X * num2 + num8;
                    int num11 = worldCoordinates.Y * num2 + num9;

                    int num12 = (num2 == 3) ? 1 : 2;
                    int num13 = 1;
                    int startX = Math.Max(num12, num10 + num13);
                    int endX = Math.Min(width - num12, num10 + num2 - num13);
                    int startY = Math.Max(num12, num11 + num13);
                    int endY = Math.Min(width - num12, num11 + num2 - num13);
                    int max = __instance.CalculateProbabilityInRange(probabilities, width, startX, endX, startY, endY);
                    int value = __instance.random.Range(0, max);
                    int num14 = __instance.IndexForProbabilityValueInRange(probabilities, width, value, startX, endX, startY, endY);

                    Loader.modLogger?.LogInfo($"[CapitalGenerator] Capital placed at {WorldCoordinates.FromIndex(num14, width)} for player {l}");

                    list2.Add(num14);
                }

                __result = new Il2CppSystem.Collections.Generic.List<int>();
                foreach (int index in list2)
                {
                    __result.Add(index);
                }

                return false;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[CapitalGenerator] Critical error: {ex}");
                return true; 
            }
        }

        // =========================================================================
        // C. Village Generation Logics
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateInternal))]
        private static void GenerateInternal_DistributeVillages(MapGenerator __instance, GameState gameState)
        {
            try
            {

                if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return;
                }

                Loader.modLogger?.LogInfo($"[Conquest-Map] ConquestVillageGeneration...");
                ConquestVillageGeneration(__instance, gameState);

            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Map] MapGenerator error: {ex.Message}");
            }
        }        
        
        private static void ConquestVillageGeneration(MapGenerator gen, GameState gameState)
        {
            try
            {
                List<TileData> neutralVillages = new List<TileData>();
                for (int i = 0; i < gameState.Map.Tiles.Length; i++)
                {
                    TileData tile = gameState.Map.Tiles[i];
                    if (tile.HasImprovement(ImprovementData.Type.City) && tile.owner == 0)
                    {
                        neutralVillages.Add(tile);
                    }
                }

                int playerCount = gameState.PlayerCount;
                if (playerCount <= 0) return;
                Loader.modLogger!.LogInfo($"[Conquest-Map] {neutralVillages.Count} villages after vanilla generation for {playerCount} players.");

                int remainder = neutralVillages.Count % playerCount;
                int citiesToSpawn = playerCount - remainder;
                Loader.modLogger!.LogInfo($"[Conquest-Map] {citiesToSpawn} more villages needed to even out distribution for all players.");

                // Tries to add villages if it is close to distribute 1 more villages to each player
                if (remainder > 0 && remainder >= (playerCount * 0.5f))
                {
                    Loader.modLogger!.LogInfo($"[Conquest-Map] Trying to add villages!");
   
                    for (int s = 0; s < citiesToSpawn; s++)
                    {
                        Loader.modLogger!.LogInfo($"[Conquest-Map] Attempting to spawn new village...");

                        WorldCoordinates emergencyCoords = gen.GetEmergencyCityPosition(gameState, gameState.Map);
                        if (emergencyCoords != WorldCoordinates.NULL_COORDINATES)
                        {
                            int tileIndex = gameState.Map.GetTileIndex(emergencyCoords);
                            TileData targetTile = gameState.Map.Tiles[tileIndex];
                            gen.SetTileAsCity(targetTile);
                            neutralVillages.Add(targetTile);
                            Loader.modLogger!.LogInfo($"[Conquest-Map] {s+1}st emergency village placed at {emergencyCoords}.");
                        }
                        else
                        {
                            Loader.modLogger!.LogInfo($"[Conquest-Map] Failure to spawn new village!");
                            break;
                        }
                    }
                    
                    Loader.modLogger!.LogInfo($"[Conquest-Map] {neutralVillages.Count} villages after custom generation for {playerCount} players.");
                }

                // Decide which village to scrap based on proximity (if necessary)
                int maxCitiesPerPlayer = neutralVillages.Count / playerCount;
                HashSet<WorldCoordinates> assignedCoordinates = new HashSet<WorldCoordinates>();

                for (int round = 0; round < maxCitiesPerPlayer; round++)
                {
                    for (int p = 0; p < playerCount; p++)
                    {
                        PlayerState player = gameState.PlayerStates[p];
                        AssignClosestVillage(gameState, neutralVillages, assignedCoordinates, player);
                    }
                }

                // Convert excess to ruins
                int ruinsCount = 0;
                for (int i = neutralVillages.Count - 1; i >= 0; i--)
                {
                    var village = neutralVillages[i];
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
                        neutralVillages.RemoveAt(i);
                        ruinsCount++;
                    }
                }

                Loader.modLogger!.LogInfo($"[Conquest-Map] ConquestVillageGeneration complete. Converted {ruinsCount} villages to ruins. {neutralVillages.Count} villages remain.");
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Map] ConquestVillageGeneration failed: {ex.Message}");
            }
        }

        // =========================================================================
        // D. City Distribution & Initialization
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(StartMatchAction), nameof(StartMatchAction.ExecuteDefault))]
        private static void StartMatchAction_InitializeVillages(StartMatchAction __instance, GameState gameState)
        {
            if (gameState?.Settings == null) return;
            try
            {
                if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return;
                }

                Loader.modLogger?.LogInfo("[Conquest-Match] Executing village initialization in StartMatchAction...");
                ConquestVillageDistribution(gameState);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Match] Critical failure in StartMatchAction: {ex.Message}");
            }
        }

        private static void ConquestVillageDistribution(GameState gameState)
        {
            List<TileData> neutralVillages = new List<TileData>();
            for (int i = 0; i < gameState.Map.Tiles.Length; i++)
            {
                TileData tile = gameState.Map.Tiles[i];
                if (tile.HasImprovement(ImprovementData.Type.City) && tile.owner == 0)
                {
                    neutralVillages.Add(tile);
                }
            }

            int playerCount = gameState.PlayerCount;
            if (playerCount == 0) return;

            int maxCitiesPerPlayer = neutralVillages.Count / playerCount;
            HashSet<WorldCoordinates> assignedCoordinates = new HashSet<WorldCoordinates>();

            Loader.modLogger?.LogInfo($"[Conquest-Match] {neutralVillages.Count} villages to be initialized. Allocating {maxCitiesPerPlayer} per player...");

            for (int round = 0; round < maxCitiesPerPlayer; round++)
            {
                for (int p = 0; p < playerCount; p++)
                {
                    PlayerState player = gameState.PlayerStates[p];
                    TileData closestVillage = AssignClosestVillage(gameState, neutralVillages, assignedCoordinates, player);

                    if (closestVillage != null)
                    {
                        ConquestInitializeCity(gameState, closestVillage, player);
                    }
                }
            }

            Loader.modLogger?.LogInfo($"[Conquest-Match] All cities initialized successfully!");
        }

        private static TileData AssignClosestVillage(
            GameState gameState, List<TileData> neutralVillages, HashSet<WorldCoordinates> assignedCoordinates, PlayerState player)
        {
            WorldCoordinates capitalCoords = player.startTile;
            TileData? closestVillage = null;
            int closestDistance = int.MaxValue;

            foreach (var village in neutralVillages)
            {
                if (assignedCoordinates.Contains(village.coordinates)) continue;

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
                return closestVillage;
            }

            return gameState.Map.GetTile(WorldCoordinates.NULL_COORDINATES);
        }

        private static void ConquestInitializeCity(GameState state, TileData tile, PlayerState player)
        {
            try
            {
                tile.owner = player.Id;
                tile.capitalOf = 0;

                TribeData tribeData;
                if (state.GameLogicData.TryGetData(player.tribe, out tribeData) && tribeData != null)
                {
                    string generatedName = MapDataExtensions.GenerateCityName(state, tile.coordinates, tribeData, player.skinType);
                    if (tile.improvement != null)
                    {
                        tile.improvement.name = generatedName;
                    }
                }

                player.cities++;

                UnitData unitData;
                if (state.GameLogicData.TryGetData(UnitData.Type.Warrior, out unitData))
                {
                    UnitState unitState = ActionUtils.TrainUnitScored(state, player, tile, unitData);
                    unitState.attacked = false;
                    unitState.moved = false;
                }

                Il2CppSystem.Collections.Generic.List<TileData> cityArea = ActionUtils.GetCityAreaSorted(state, tile);
                if (cityArea != null)
                {
                    for (int j = 0; j < cityArea.Count; j++)
                    {
                        TileData territoryTile = cityArea[j];
                        if (territoryTile != null)
                        {
                            territoryTile.owner = player.Id;
                            territoryTile.rulingCityCoordinates = tile.coordinates;
                        }
                    }
                }

                ActionUtils.RuleArea(state, player, tile, true);
                ActionUtils.ExploreFromTile(state, player, tile, 2, true);

                Loader.modLogger?.LogInfo($"[Conquest-Match] City initialized for Player {player.Id} at {tile.coordinates}.");
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Match] City initialization failed: {ex.Message}");
            }
        }

        // =========================================================================
        // E. Citadel Logics
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameLogicData), nameof(GameLogicData.CanBuild))]
        private static void CanBuild_Citadel(GameLogicData __instance, GameState gameState, TileData tile, PlayerState playerState, ImprovementData improvement, ref bool __result)
        {
            if (tile.improvement != null && improvement.type != ImprovementData.Type.Road) 
            {
                __result = false;
                return;
            }

			if (improvement.HasAbility(ImprovementAbility.Type.Limited) && __instance.HasImprovementWithinCityBorders(gameState.Map, tile.rulingCityCoordinates, improvement.type))
			{
                __result = false;
				return;
			}

            try
            {
                if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return;
                }

                if (improvement.type == EnumCache<ImprovementData.Type>.GetType("citadel") && tile.owner == playerState.Id)
                {
                    int cityLimit = 0;
                    int capitalLimit = 0;
                    int citadel = CountCityCitadel(gameState, tile);
                    TileData cityTile = GameManager.GameState.Map.GetTile(tile.rulingCityCoordinates);
                    
                    if (gameState.Settings.MapSize  <= 11)
                    {
                        cityLimit = 1;
                        capitalLimit = 1;
                    }
                    else
                    if (gameState.Settings.MapSize  <= 16)
                    {
                        cityLimit = 1;
                        capitalLimit = 2;
                    }
                    else
                    if (gameState.Settings.MapSize  <= 20)
                    {
                        cityLimit = 2;
                        capitalLimit = 2;
                    }
                    else
                    {
                        cityLimit = 3;
                        capitalLimit = 3;
                    }

                    if (tile.terrain == TerrainData.Type.Mountain && !playerState.HasAbility(EnumCache<PlayerAbility.Type>.GetType("mountaincitadel"), gameState))
                    {
                        __result = false;
                        return;
                    }
                    if (tile.terrain == TerrainData.Type.Water && !playerState.HasAbility(EnumCache<PlayerAbility.Type>.GetType("watercitadel"), gameState))
                    {
                        __result = false;
                        return;
                    }

                    if (cityTile.capitalOf != 0 && citadel >= capitalLimit)
                    {
                        __result = false;
                        return;
                    }
                    if (cityTile.capitalOf == 0 && citadel >= cityLimit)
                    {
                        __result = false;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest] Error in CanBuild Postfix: {ex}");
            }            
        }   

        /*[HarmonyPrefix]
        [HarmonyPatch(typeof(BuildAction), nameof(BuildAction.ExecuteDefault))]
        private static bool BuildAction__Prefix(BuildAction __instance, GameState gameState)
        {
            try
            {
                if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return true;
                }

                TileData tile = gameState.Map.GetTile(__instance.Coordinates);
                ImprovementData improvementData;
                PlayerState playerState;
				if (tile != null && gameState.GameLogicData.TryGetData(__instance.Type, out improvementData) && gameState.TryGetPlayer(__instance.PlayerId, out playerState))
		        {

                    if (improvementData.type != EnumCache<ImprovementData.Type>.GetType("citadel"))
                    {
                        return true;
                    }

                    int num = CountCityCitadel(gameState, tile);   
                    ImprovementState improvementState = new ImprovementState
                    {
                        type = __instance.Type,
                        borderSize = (ushort)improvementData.borderSize,
                        level = 0,
                        xp = 0,
                        production = 1,
                        founded = (ushort)gameState.CurrentTurn,
                        baseScore = (ushort)improvementData.GetScoreReward(),
                        founder = __instance.PlayerId
                    };
                    tile.improvement = improvementState;
                    if (__instance.DeductCost)
                    {
                        playerState.Currency -= improvementData.GetCurrencyCost() + num * 2;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest] Error in BuildAction Prefix: {ex}");
                return true;
            }
        }*/

        [HarmonyPostfix]
        [HarmonyPatch(typeof(BuildAction), nameof(BuildAction.ExecuteDefault))]
        private static void BuildAction_Citadel(BuildAction __instance, GameState gameState)
        {
            try
            {
                if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return;
                }

                TileData tile = gameState.Map.GetTile(__instance.Coordinates);
                ImprovementData improvementData;
                PlayerState playerState;
				if (tile != null && gameState.GameLogicData.TryGetData(__instance.Type, out improvementData) && gameState.TryGetPlayer(__instance.PlayerId, out playerState))
		        {

                    if (improvementData.type != EnumCache<ImprovementData.Type>.GetType("citadel"))
                    {
                        return;
                    }

                    TileData cityTile = GameManager.GameState.Map.GetTile(tile.rulingCityCoordinates);
                    int area = cityTile.improvement.borderSize;
                    ActionUtils.ExploreFromTile(gameState, playerState, tile, area, true);
                    
                    TileData[] areaSorted = gameState.Map.GetAreaSorted(tile.coordinates, area, true, true);
                    if (areaSorted != null && areaSorted.Length > 0)
                    {
                        foreach (TileData tileData in areaSorted)
                        {
                            if (tileData.owner == 0)
                            {
                                tileData.owner = __instance.PlayerId;
                                tileData.rulingCityCoordinates = cityTile.coordinates;
                                
                                Tile instance = tileData.GetInstance();
                                if (instance != null)
                                {
                                    instance.Render();
                                }
                            }
                        }

                        foreach (TileData tileData in areaSorted)
                        {
                            Tile instance = tileData.GetInstance();
                            if (instance != null)
                            {
                                instance.Render();
                            }
                        }
                        // ActionUtils.RuleArea(gameState, playerState, tile, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest] Error in BuildAction Prefix: {ex}");
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(DestroyImprovementAction), nameof(DestroyImprovementAction.ExecuteDefault))]
        private static bool DestroyImprovementAction_Citadel(DestroyImprovementAction __instance, GameState state)
        {
            try
            {
                if (state.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && state.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return true;
                }

                TileData tile = state.Map.GetTile(__instance.Coordinates);
            
                if (tile.improvement.type != EnumCache<ImprovementData.Type>.GetType("citadel"))
                {
                    return true;
                }

                TileData cityTile = GameManager.GameState.Map.GetTile(tile.rulingCityCoordinates);
                int area = cityTile.improvement.borderSize;
                TileData[] areaSorted = state.Map.GetAreaSorted(tile.coordinates, area, true, true);
                
                if (areaSorted != null && areaSorted.Length > 0)
                {
                    foreach (TileData tileData in areaSorted)
                    {
                        if (tileData.owner == cityTile.owner && tileData.rulingCityCoordinates == cityTile.coordinates)
                        {
                            TileData[] areaSorted2 = state.Map.GetAreaSorted(tileData.coordinates, area, true, true);
                            if (areaSorted2 == null) continue;

                            bool isRule = false;

                            foreach (TileData tileData2 in areaSorted2)
                            {
                                if (tileData2.improvement != null && (tileData2.improvement.type == ImprovementData.Type.City || tileData2.improvement.type == EnumCache<ImprovementData.Type>.GetType("citadel"))
                                    && tileData2.owner == cityTile.owner
                                    && tileData2.rulingCityCoordinates == cityTile.coordinates
                                    && tileData2.coordinates != tile.coordinates)
                                {
                                    isRule = true;
                                    break;
                                }
                            }

                            if (!isRule)
                            {
                                int num = ScoreSheet.tileValue;
                                if (tileData.improvement != null)
                                {
                                    num += state.CalculateImprovementScore(tileData);
                                }
                                state.ActionStack.Add(new DecreaseScoreAction(tileData.owner, num));

                                ImprovementData improvementData;
                                if (tileData.improvement != null && state.GameLogicData.TryGetData(tileData.improvement.type, out improvementData))
                                {
                                    int num2 = improvementData.CalculateImprovementPopulationAtLevel(tileData.improvement.level);
                                    for (int i = 0; i < num2; i++)
                                    {
                                        state.ActionStack.Add(new DecreasePopulationAction(tileData.owner, tileData.rulingCityCoordinates, 200));
                                    }
                                }

                                tileData.owner = 0;
                                tileData.rulingCityCoordinates = WorldCoordinates.NULL_COORDINATES; 
                                tileData.improvement = null;
                            }
                        }
                    }

                    foreach (TileData tileData in areaSorted)
                    {
                        Tile instance = tileData.GetInstance();
                        if (instance != null)
                        {
                            instance.Render();
                        }
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest] Error in BuildAction Prefix: {ex}");
                return true;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UnitDataExtensions), nameof(UnitDataExtensions.GetDefenceBonus))]
        private static void GetDefenceBonus_Citadel(UnitState unit, GameState gameState, ref int __result)
        {
            TileData tile = gameState.Map.GetTile(unit.coordinates);
            if (tile == null)
            {
                return;
            }

            if (tile != null && tile.improvement != null && tile.improvement.type == EnumCache<ImprovementData.Type>.GetType("citadel") && tile.owner == unit.owner)
            {
                __result = 15;
            }

            if (tile != null && tile.improvement != null && tile.improvement.type == EnumCache<ImprovementData.Type>.GetType("citadel") && tile.terrain == TerrainData.Type.Mountain && tile.unit.UnitData.attack <= 3 && tile.owner == unit.owner)
            {
                __result = 40;
            }

            if (tile != null && tile.improvement != null && tile.improvement.type == EnumCache<ImprovementData.Type>.GetType("citadel") && tile.terrain == TerrainData.Type.Water && tile.unit.UnitData.attack <= 3 && tile.owner == unit.owner)
            {
                __result = 40;
            }
        }

        /*[HarmonyPostfix]
        [HarmonyPatch(typeof(TrainCommand), nameof(TrainCommand.IsValid))]
        private static void TrainCommand_Citadel(TrainCommand __instance, GameState state, ref bool __result, string validationError)
        {
            TileData tile = state.Map.GetTile(__instance.Coordinates);
            if (tile.improvement != null && tile.improvement.type == EnumCache<ImprovementData.Type>.GetType("citadel")
                && tile.owner == __instance.PlayerId
                && tile.unit == null)
            {
                UnitData unitData;
                if (state.GameLogicData.TryGetData(__instance.Type, out unitData))
                {
                    if (unitData.cost != 8)
                    {
                        __result = true;
                        return;                        
                    }
                }
            }
        }*/

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CommandUtils), nameof(CommandUtils.GetTrainableUnits))]
        private static void GetTrainableUnits_Citadel(GameState gameState, PlayerState player, TileData tile, ref Il2CppSystem.Collections.Generic.List<TrainCommand> __result, bool includeUnavailable = false)
        {
            Il2CppSystem.Collections.Generic.List<TrainCommand> list = new Il2CppSystem.Collections.Generic.List<TrainCommand>();
            if (tile.improvement != null && tile.improvement.type == EnumCache<ImprovementData.Type>.GetType("citadel"))
                {
                    if (tile.owner != player.Id)
                    {
                        return;
                    }

                    if (tile.terrain == TerrainData.Type.Water)
                    {
                        return;
                    }

                    foreach (UnitData unitData in gameState.GameLogicData.GetUnlockedUnits(player, gameState, false))
                    {
                        if (CommandValidation.HasUnitTerrain(gameState, tile.coordinates, unitData) && unitData.cost != 8)
                        {
                            TrainCommand trainCommand = new TrainCommand(player.Id, unitData.type, tile.coordinates);
                            if (!player.blockTrainUnits && (includeUnavailable || trainCommand.IsValid(gameState)))
                            {
                                list.Add(trainCommand);
                            }
                        }
                    }
                    __result = list;
                    return;
                }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ActionUtils), nameof(ActionUtils.TrainUnit))]
        private static void TrainUnit_FindHome(GameState gameState, PlayerState playerState, TileData tile, UnitData unitData, UnitState __result)
        {
            try
            {
                if (__result == null || tile == null) return;

                if (tile.improvement != null && tile.improvement.type == EnumCache<ImprovementData.Type>.GetType("citadel"))
                {
                    __result.home = tile.rulingCityCoordinates;
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Train] Shielded error in TrainUnit Postfix: {ex.Message}");
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ActionUtils), nameof(ActionUtils.GetCityAreaSorted))]
        private static bool GetCityAreaSorted_Conquest(GameState gameState, TileData cityTile, ref Il2CppSystem.Collections.Generic.List<TileData> __result)
        {
            try
            {
                if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return true;
                }

                PlayerState player;
                if (gameState.TryGetPlayer(cityTile.owner, out player))
                {
                    WorldCoordinates centerCoords = (cityTile.rulingCityCoordinates == WorldCoordinates.NULL_COORDINATES) 
                        ? cityTile.coordinates 
                        : cityTile.rulingCityCoordinates;

                    TileData cityCenter = GameManager.GameState.Map.GetTile(centerCoords);
                    
                    if (cityCenter == null) return true; 

                    Il2CppSystem.Collections.Generic.List<TileData> list = new Il2CppSystem.Collections.Generic.List<TileData>();
                    TileData[] areaSorted = gameState.Map.GetAreaSorted(cityCenter.coordinates, 8, true, true);
                    
                    if (areaSorted != null && areaSorted.Length > 0)
                    {
                        foreach (TileData tileData in areaSorted)
                        {
                            if (tileData.rulingCityCoordinates == cityCenter.coordinates || tileData.coordinates == cityCenter.coordinates)
                            {
                                list.Add(tileData);
                            }
                        }
                    }

                    __result = list; 
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest] Error in GetCityAreaSorted Prefix: {ex}");
                return true;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GameLogicData), nameof(GameLogicData.HasImprovementWithinCityBorders))]
        private static bool HasImprovementWithinCityBorders_Conquest(MapData map, WorldCoordinates cityCoordinates, ImprovementData.Type improvementType, ref bool __result)
        {
            try
            {
                TileData cityTile = map.GetTile(cityCoordinates);
                Il2CppSystem.Collections.Generic.List<TileData> cityArea = ActionUtils.GetCityAreaSorted(GameManager.GameState, cityTile);
                for (int i = 0; i < cityArea.Count; i++)
                {
                    TileData tileData = cityArea[i];
                    if (!(tileData.rulingCityCoordinates != cityCoordinates) && tileData.HasImprovement(improvementType))
                    {
                        __result = true;
                        return false;
                    }
                }
                __result = false;
                return false;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest] Error in HasImprovementWithinCityBorders Prefix: {ex}");
                return true;
            }
        }

        public static int CountCityCitadel(GameState gameState, TileData tile)
        {
            Il2CppSystem.Collections.Generic.List<TileData> cityArea = ActionUtils.GetCityAreaSorted(gameState, tile);
            int count = 0;
            if (cityArea != null)
            {
                foreach (TileData territoryTile in cityArea)
                {
                    if (territoryTile != null && territoryTile.improvement != null)
                    {
                        if (territoryTile.improvement.type == EnumCache<ImprovementData.Type>.GetType("citadel"))
                        {
                            count++;
                            // Loader.modLogger?.LogInfo($"Citadel count is {count} on tile {territoryTile.coordinates}");
                        }
                    }
                }
            }      
            return count;
        }

        // =========================================================================
        // F. Tech Cost & City Destruction Handler
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameLogicData), nameof(GameLogicData.GetTechPrice))]
        private static void GetTechPrice_Conquest(GameLogicData __instance, TechData techData, PlayerState playerState, GameState state, ref int __result)
        {
            if (state == null || techData == null) return;
            try
            {
                if (GameManager.GameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && GameManager.GameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return;
                };

                float num = Math.Max(4 + techData.cost, playerState.cities + state.CurrentTurn * techData.cost);
                num = (float)Math.Min(num, techData.cost * (playerState.cities + 2) * 2);
                
                if (__instance.HasAbility(playerState, PlayerAbility.Type.Literacy))
                {
                    float num2 = 0.66666f;
                    num *= num2;
                }
                __result = (int)Math.Ceiling((double)num);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Tech] Error: {ex.Message}");
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CaptureCityAction), nameof(CaptureCityAction.ExecuteDefault))]
        private static bool CaptureCityAction_Conquest(CaptureCityAction __instance, GameState gameState)
        {
            if (gameState?.Settings == null) return true;
            try
            {
                if (GameManager.GameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && GameManager.GameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return true;
                };

                TileData cityTile = gameState.Map.GetTile(__instance.Coordinates);
                PlayerState? attacker = null;
                gameState.TryGetPlayer(__instance.PlayerId, out attacker);

                if (cityTile != null && attacker != null)
                    DestroyCityConquest(gameState, cityTile, attacker, false);

                return false;
            }
            catch
            {
                return true;
            }
        }

        public static void DestroyCityConquest(GameState gameState, TileData cityTile, PlayerState playerState, bool isCityUpgrade)
        {
            if (cityTile?.improvement?.type != ImprovementData.Type.City) return;

            // 1. Fetch original owner & population
            int transferredPopulation = 0;
            byte originalOwnerId = cityTile.owner;
            PlayerState originalOwner;
            gameState.TryGetPlayer(originalOwnerId, out originalOwner);

            if (originalOwner != null)
            {
                transferredPopulation = cityTile.improvement.population; 

                if (originalOwner.cities > 0)
                {
                    originalOwner.cities--;
                    Loader.modLogger?.LogInfo($"[Conquest] Player {originalOwner.Id} lost a city. Total remaining: {originalOwner.cities}");
                }
            }

            // 2. Transfer population to nearest unsieged city (or capital)
            if (transferredPopulation > 0 && originalOwner != null)
            {
                TileData? fleeCityTile = null;
                int closestDistance = int.MaxValue;

                for (int i = 0; i < gameState.Map.Tiles.Length; i++)
                {
                    TileData tile = gameState.Map.Tiles[i];
                    
                    if (tile.HasImprovement(ImprovementData.Type.City) && tile.owner == originalOwnerId && tile.coordinates != cityTile.coordinates)
                    {
                        bool isSieged = false;
                        
                        if (tile.unit != null && tile.unit.owner != originalOwnerId)
                        {
                            isSieged = true;
                        }

                        if (!isSieged)
                        {
                            int distance = MapDataExtensions.ManhattanDistance(cityTile.coordinates, tile.coordinates);
                            if (distance < closestDistance)
                            {
                                closestDistance = distance;
                                fleeCityTile = tile;
                            }
                        }
                    }
                }
                if (isCityUpgrade == false)
                {
                    if (fleeCityTile != null)
                    {
                        fleeCityTile.improvement.AddPopulation((short)transferredPopulation);
                        Loader.modLogger?.LogInfo($"[Conquest] Transferred {transferredPopulation} populations from razed city to safe city at {fleeCityTile.coordinates}.");

                    }
                    else
                    {
                        Loader.modLogger?.LogInfo($"[Conquest] No safe, un-sieged cities found for Player {originalOwnerId}. Population permanently lost.");
                    }
                }
                else
                {
                    TileData capital = GameManager.GameState.Map.GetTile(playerState.startTile);
                    if (capital != null)
                    {
                        for (int j = 0; j < 2; j++)
                        {
                            gameState.ActionStack.Add(new IncreasePopulationAction(playerState.Id, cityTile.coordinates, capital.coordinates, 60));
                            //instance.AddSubAction(new IncreasePopulationAction(playerState.Id, cityTile.coordinates, capital.coordinates, 60));
                        }
                        playerState.currency += 3;
                        Loader.modLogger?.LogInfo($"[Conquest-Tech] Transferred 2 populations from abandoned city to capital at {capital.coordinates}.");

                    }
                    else
                    {
                        Loader.modLogger?.LogInfo($"[Conquest-Tech] Capital not owned by Player {originalOwnerId}. Population permanently lost.");
                    }
                }
            }

            // 3. Rewards & Scores increment for attacker
            int reward = Math.Min(15, cityTile.improvement.level * 2) + Math.Min(15, (int)gameState.CurrentTurn);
            int score  = 100 + cityTile.improvement.level * 50;
            gameState.ActionStack.Add(new IncreaseScoreAction(playerState.Id, score, cityTile.coordinates, 50));

            if (playerState != null && !isCityUpgrade)
            {
                playerState.Currency += reward;
                Loader.modLogger?.LogInfo($"[Conquest] City destroyed by player {playerState.Id} (+{reward} stars & {score} scores)");
            }

            // 4. Unrule city area & Score deduction for defender
            Il2CppSystem.Collections.Generic.List<TileData> cityArea = ActionUtils.GetCityAreaSorted(gameState, cityTile);
            if (cityArea != null)
            {
                for (int j = 0; j < cityArea.Count; j++)
                {
                    TileData territoryTile = cityArea[j];
                    // Loader.modLogger?.LogInfo($"[Conquest] Unrule action for {territoryTile.coordinates}");
                    if (territoryTile != null)
                    {
                        int num = ScoreSheet.tileValue;
                        if (territoryTile.improvement != null && territoryTile.coordinates != cityTile.coordinates)
                        {
                            num += gameState.CalculateImprovementScore(territoryTile);
                        }
                        gameState.ActionStack.Add(new DecreaseScoreAction(territoryTile.owner, num));

                        territoryTile.owner = 0;
                        territoryTile.rulingCityCoordinates = WorldCoordinates.NULL_COORDINATES; 
                        if (territoryTile != null && territoryTile.improvement != null && territoryTile.improvement.type != ImprovementData.Type.LightHouse)
                        {
                            territoryTile.improvement = null;
                        }
                    }
                }
            }

            if (cityArea != null)
            {
                for (int i = cityArea.Count - 1; i >= 0; i--)
                {
                    Tile instance2 = cityArea[i].GetInstance();
                    if (instance2 != null)
                    {
                        instance2.Render();
                    }
                }
            }

            // 5. Generate ruins
            if (isCityUpgrade != true)
            {
                cityTile.improvement = new ImprovementState
                {
                    type = ImprovementData.Type.Ruin,
                    borderSize = 0,
                    level = 1,
                    production = 1,
                    founded = 0
                };
            }
            else
            {
                // cityTile.improvement = new ImprovementState { type = ImprovementData.Type.None };
                cityTile.improvement = null;
            }

            cityTile.owner = 0;
            // cityTile.capitalOf = 0;  // leave mark of capital

            // 6. Wipe all other cities if pass/multi
            if (playerState != null && originalOwner != null && cityTile.capitalOf != 0 && gameState.Settings.RulesGameMode == EnumCache<GameMode>.GetType("reign")) {
                Il2CppSystem.Collections.Generic.List<TileData> cityList = originalOwner.GetCityTiles(gameState);
                foreach (TileData targetTile in cityList) {
                    // gameState.ActionStack.Add(new CaptureCityAction(attacker.Id, targetTile.coordinates, originalOwner.Id));
                    DestroyCityConquest(gameState, targetTile, playerState, false);
                }
            }

            // 7. Wipe player if necessary
            if (originalOwner != null && playerState != null && !originalOwner.IsAlive(gameState, gameState.Settings.rules.PlayerDeathCondition))
            {
                originalOwner.wipedAtCommandIndex = gameState.CommandStack.Count - 1;
                gameState.ActionStack.Add(new WipePlayerAction(playerState.Id, originalOwner.Id));
            }
            
            Loader.modLogger?.LogInfo($"[Conquest] City at {cityTile.coordinates} has been successfully razed.");
        }

        // =========================================================================
        // G. Win Conditions
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameState), nameof(GameState.TryGetWinner))]
        private static void TryGetWinner_Conquest(GameState __instance, ref bool __result, ref PlayerState winner)
        {
            if (__result) return;

            if (__instance == null || __instance.Settings == null) return;

            try
            {
                var playersSortedByRank = __instance.GetPlayersSortedByRank();
                if (playersSortedByRank == null || playersSortedByRank.Count == 0) return;

                PlayerState topWinner = playersSortedByRank[0];
                if (topWinner == null) return;

                if (__instance.Settings.RulesGameMode == EnumCache<GameMode>.GetType("reign"))
                {
                    int num = GameStateUtils.CountAlivePlayers(__instance); 

                    if (num <= 1 && topWinner.CountCapitals(__instance) == 1)
                    {
                        winner = topWinner;
                        __result = true;
                        return;
                    }
                }

                /*if (__instance.Settings.rules.ScoreLimit > 0 && topWinner.score >= (ulong)__instance.Settings.rules.ScoreLimit)
                {
                    winner = topWinner;
                    __result = true;
                    return;
                }
                */
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-AI] Error in TryGetWinner Postfix: {ex}");
            }
        }

        // =========================================================================
        // H. Reactions
        // =========================================================================
        private static Il2CppSystem.Action? _activePopupCallbackHolder;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CaptureCityReaction), nameof(CaptureCityReaction.Execute))]
        public static bool CaptureCityReaction_Conquest(CaptureCityReaction __instance, Il2CppSystem.Action onComplete)
        {
            try
            {
                Loader.modLogger?.LogInfo("[Conquest-Popup] CaptureCityReaction started.");

                if (GameManager.GameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && GameManager.GameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return true;
                }

                TileData tile = GameManager.GameState.Map.GetTile(__instance.action.Coordinates);
                PlayerState playerState;
                GameManager.GameState.TryGetPlayer(__instance.action.PlayerId, out playerState);
                PlayerState prevOwnerState;
                bool hasPreviousOwner = GameManager.GameState.TryGetPlayer(__instance.action.OldOwnerId, out prevOwnerState);
                bool isPreviousOwnerCapital = hasPreviousOwner && tile.capitalOf == __instance.action.OldOwnerId;
                bool flag = isPreviousOwnerCapital && GameManager.IsPlayerViewing(__instance.action.OldOwnerId) && !GameManager.Client.IsSpectating;
                Tile instance = tile.GetInstance();
                byte attackerId = __instance.action.PlayerId;

                // Visuals
                if (instance != null)
                {
                    AudioManager.PlaySFXAtTile(SFXTypes.Capture, tile.coordinates, 0, 1f, 1f);
                    instance.Render();
                    instance.SpawnShine(2f);
                    instance.SpawnSparkles(2f);
                    instance.StopFire();

                    ReactionUtils.UpdateSurroundingBordersAndTransportPaths(attackerId, tile);
                    ResourceManager.AddResourceOfTypeToResourceBar(attackerId, ResourceManager.Type.Score, __instance.action.Score, tile.coordinates, null, "None");

                    // Temp Pointer Holder
                    _activePopupCallbackHolder = onComplete;
                    ExecutePopupLogic(__instance, _activePopupCallbackHolder, tile, playerState, prevOwnerState, isPreviousOwnerCapital, instance, attackerId);
                }

                Il2CppSystem.Collections.Generic.List<TileData> areaSorted = ActionUtils.GetCityAreaSorted(GameManager.GameState, tile);
                if (areaSorted != null)
                {
                    for (int i = areaSorted.Count - 1; i >= 0; i--)
                    {
                        Tile instance2 = areaSorted[i].GetInstance();
                        instance2.Render();
                    }
                }

                if (tile.unit != null)
                {
                    Tile tileInstance = MapRenderer.Current.GetTileInstance(__instance.action.PreviousHomeTown);
                    if (tileInstance != null && !tileInstance.IsHidden)
                    {
                        tileInstance.Render();
                    }
                }
                if (!GameManager.Client.IsReplay)
                {
                    InputEvents.SelectionCleared();
                    ResourceManager.IncomeChanged(__instance.action.PlayerId);
                }
                if (!flag)
                {
                    GameManager.DelayCall(2500, onComplete);
                }
                return false;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Popup] CaptureCityReaction error: {ex}");
                return true;
            }
        }

        private static void ExecutePopupLogic(
            CaptureCityReaction __instance,
            Il2CppSystem.Action onComplete,
            TileData tile,
            PlayerState playerState,
            PlayerState prevOwnerState,
            bool isPreviousOwnerCapital,
            Tile instance,
            int attackerId)
        {
            try
            {
                Loader.modLogger?.LogInfo("[Conquest-Popup] ExecutePopupLogic started.");

                if (GameManager.IsPlayerViewing((byte)attackerId) && !GameManager.Client.IsSpectating)
                {
                    CameraController.Instance.CenterOnPosition(tile.coordinates.ToPosition(), 0.8f, null, false);

                    // Attacker - No button
                    string tribeName = prevOwnerState.tribe.GetName();;
                    string capitalized = char.ToUpper(tribeName[0]) + tribeName.Substring(1);
                    
                    string title = isPreviousOwnerCapital ? "Good News!" : "City Razed!";
                    string message = isPreviousOwnerCapital 
                        ? $"You have razed the {capitalized} capital! All their trade connections are destroyed forever." 
                        : $"The city is now a ruin on the ground.";
                    int time = isPreviousOwnerCapital ? 5 : 3;
                    
                    NotificationBase ntf = NotificationManager.GetBasicNotification();
                    ntf.header.text = title;
                    ntf.description.text = message;
                    ntf.showTime = time;     
                    ntf.Show(); 
                }
                else if (GameManager.IsPlayerViewing(__instance.action.OldOwnerId) && !GameManager.Client.IsSpectating)
                {
                    CameraController.Instance.CenterOnPosition(tile.coordinates.ToPosition(), 0.8f, null, false);

                    // Defender - With button
                    string linkedTribeNameWithSpace = playerState.GetLinkedTribeNameWithSpace(GameManager.GameState);
                    
                    string title = isPreviousOwnerCapital ? "Bad News!" : "City Razed!";
                    string message = isPreviousOwnerCapital 
                        ? $"Your capital has fallen to {linkedTribeNameWithSpace}. All your trade connections are lost forever." 
                        : $"Your city is wiped out from existence.";

                    if (!isPreviousOwnerCapital) 
                    {
                        NotificationBase ntf = NotificationManager.GetBasicNotification();
                        ntf.header.text = title;
                        ntf.description.text = message;
                        ntf.showTime = 3;       
                        ntf.Show();       
                    } 
                    else 
                    {
                        BasicPopup basicPopup = PopupManager.GetBasicPopup();
                        basicPopup.sprite = UIManager.IconData.GetSprite("CapitalCapture");
                        basicPopup.Header = title;
                        basicPopup.Description = message;
                        basicPopup.SetTribeInfoButtons(TextType.Description);
                        basicPopup.buttonData = new PopupBase.PopupButtonData[]
                        {
                            new PopupBase.PopupButtonData("buttons.ok", PopupBase.PopupButtonData.States.Selected, onComplete, -1, true, null)
                        };
                        Loader.modLogger?.LogInfo($"ButtonData is {basicPopup.buttonData}");

                        basicPopup.Show(InputManager.GetInputPosition());
                        Loader.modLogger?.LogInfo("[Conquest-Backend] ExecutePopupLogic finished!");
                    }
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Popup] ExecutePopupLogic error: {ex}");
                onComplete?.Invoke();
            }
        }
    }
}