using HarmonyLib;
using Il2CppInterop.Runtime.Runtime;
using Polytopia.Data;
using PolytopiaBackendBase;
using PolytopiaBackendBase.Game;

namespace PolyMode
{
    public static class AI_2
    {
        // =========================================================================
        // A. Diplomacy Behaviors
        // =========================================================================
        [HarmonyPrefix]
        [HarmonyPatch(typeof(AI), nameof(AI.GetGameProgress))]
        private static bool GetGameProgress_Conquest(ref float __result, GameState gameState, PlayerState winningPlayer)
        {
            if (gameState?.Settings == null) return true;

            try
            {
                if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return true;
                }
                
                if (winningPlayer == null)
                {
                    __result = 0f;
                    return false;
                }

                float totalCities = Math.Max(0.1f, (float)MapDataExtensions.CountCities(gameState));
                float cityProgress = (float)winningPlayer.cities / totalCities;
                
                __result = Math.Min(1f, Math.Max(0f, cityProgress));
                
                return false; 

            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-AI] Error in GetGameProgress detour: {ex.Message}");
                __result = 0f; 
                return false;   // DON'T allow vanilla code run because custom gamemode cause crash
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(OpinionManager), nameof(OpinionManager.UpdateOpinion))]
        private static void UpdateOpinion_Cities(OpinionManager __instance, GameState gameState, PlayerState player, PlayerState opponent)
        {
            if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
            {
                return;
            }
            if (player == opponent)
            {
                return;
            }
            if (player.Id == 255)
            {
                return;
            }
            if (opponent.Id == 255)
            {
                return;
            }
            if (!opponent.IsAlive(gameState))
            {
                return;
            }
            if (player.GetRelation(opponent.Id).FirstMeet < 0 && opponent.GetRelation(player.Id).LastAttackTurn < 0)
            {
                return;
            }

            // Extra hate for players with more owned cities than avg
            float cityAdvantage = opponent.GetCityAdvantage(gameState);
            float hate = cityAdvantage * 1f;
            // Loader.modLogger?.LogInfo($"City advantage of player {opponent.Id} = {cityAdvantage}");

            if (cityAdvantage > 0)
            {
                OpinionState opinionState = new OpinionState();
                opinionState.AddOpinion((float)(hate * 2.3), EnumCache<OpinionManager.Type>.GetType("obstinate"));
                opinionState.AddOpinion((float)(hate * 1.2), OpinionManager.Type.Winning);

                if (!__instance.Opinions.ContainsKey(opponent.Id))
                {
                    __instance.Opinions[opponent.Id] = new OpinionState();
                }
                __instance.Opinions[opponent.Id].AddOpinion(opinionState.GetOpinion(OpinionManager.Type.Winning) * -1f, OpinionManager.Type.Winning);
                __instance.Opinions[opponent.Id].AddOpinion(opinionState.GetOpinion(EnumCache<OpinionManager.Type>.GetType("obstinate")) * -1f, EnumCache<OpinionManager.Type>.GetType("obstinate"));

                // Loader.modLogger?.LogInfo($"Dominating opinion to player {opponent.Id} = {__instance.Opinions[opponent.Id].GetOpinion(OpinionManager.Type.Winning)}");
                // Loader.modLogger?.LogInfo($"obstinate opinion to player {opponent.Id} = {__instance.Opinions[opponent.Id].GetOpinion(EnumCache<OpinionManager.Type>.GetType("obstinate"))}");
            }
        }

        // 1. Boost attack score against rich players
        [HarmonyPostfix]
        [HarmonyPatch(typeof(AI), nameof(AI.RateBattle))]
        private static void RateBattle_Cities(GameState gameState, UnitState attackingUnit, TileData defendingTile, ref float __result)
        {
            if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
            {
                return;
            }
            if (defendingTile.owner == 0) return;

            float cityAdv = defendingTile.owner != 0 ? 
                gameState.PlayerStates[defendingTile.owner].GetCityAdvantage(gameState) : 0f;

            if (cityAdv > 0f)
            {
                __result += cityAdv * 2f;
                // Loader.modLogger?.LogInfo($"Battle hostility to player {defendingTile.owner} = {__result}");
            }
        }

        // 2. Boost capture missions against strong players !!! CRASHES
        /*[HarmonyPostfix]
        [HarmonyPatch(typeof(AI), nameof(AI.MakeMissionsForCity))]
        private static void MakeMissionsForCity_Postfix(GameState gameState, TileData tileData, PlayerState player)
        {
            if (tileData.owner == player.Id || tileData.owner == 0 || player.HasPeaceWith(tileData.owner))
                return;

            float cityAdv = gameState.PlayerStates[tileData.owner].GetCityAdvantage(gameState);
            if (cityAdv > 0f)
            {
                // Find the last added capture mission and boost it
                for (int i = player.aiState.missions.Count - 1; i >= 0; i--)
                {
                    var mission = player.aiState.missions[i];
                    if (mission.tile == tileData && mission.type == AI.MissionType.Capture)
                    {
                        mission.score += cityAdv * 25f;
                        player.aiState.missions[i] = mission;
                        break;
                    }
                }
            }
        }

        // 3. Global aggression boost in AnalyzeSituation (NOT working)
        [HarmonyPostfix]
        [HarmonyPatch(typeof(AI), nameof(AI.AnalyzeSituation))]
        private static void AnalyzeSituation_Postfix(GameState gameState, PlayerState player)
        {
            foreach (var mission in player.aiState.missions)
            {
                if (mission.type == AI.MissionType.Capture && mission.tile.owner != 0)
                {
                    float adv = gameState.PlayerStates[mission.tile.owner].GetCityAdvantage(gameState);
                    if (adv > 0f)
                    {
                        mission.score += adv * 25f;
                    }
                }
            }

            if (player.aiState.missions != null && player.aiState.missions.Count > 0)
            {
                var sortedMissions = player.aiState.missions.ToArray()
                    .OrderByDescending(m => m.score)
                    .ToArray();

                for (int i = 0; i < sortedMissions.Length; i++)
                {
                    player.aiState.missions[i] = sortedMissions[i];
                }
            }

            if (player.aiState.hitList != null && player.aiState.hitList.Count > 0)
            {
                var sortedHitList = player.aiState.hitList.ToArray()
                    .OrderByDescending(t => t.score)
                    .ToArray();

                for (int i = 0; i < sortedHitList.Length; i++)
                {
                    player.aiState.hitList[i] = sortedHitList[i];
                }
            }
        }*/

        public static float GetAverageCities(GameState state)
        {
            int total = 0;
            foreach (PlayerState p in state.PlayerStates)
            {
                total += p.IsAlive(state) ? p.CountCities(state) : 0;
            }
            return state.PlayerStates.Count > 0 ? (float)total / (state.PlayerStates.Count - 1) : 0f;
        }

        public static float GetCityAdvantage(this PlayerState player, GameState state)
        {
            return player.CountCities(state) - GetAverageCities(state);
        }

        // =========================================================================
        // B. Development Behaviors
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(AI), nameof(AI.ChooseCityReward))]
        private static void ChooseCityReward_Conquest(GameState gameState, TileData tile, CityReward[] rewards, ref CityReward __result)
        {
            if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
            {
                return;
            }
            
            GameLogicData gld = gameState.GameLogicData;
            
            System.Random random = new System.Random();

            PlayerState playerState;
            if (!gameState.TryGetPlayer(tile.owner, out playerState) || !GameManager.GameState.GameLogicData.TryGetData(playerState.tribe, out TribeData tribeData))
            {
                return;
            }
            if (tile.improvement.level == 2)
            {
                CityAnalysisResult? centerResult = MapAnalysis.ScanCity(gameState.Map, gameState, tile, 3, true, playerState);
                // MapAnalysis.LogAnalysisResult(tile, centerResult, 3);

                if (centerResult != null && centerResult.EnemyCityCount >= 2 && centerResult.EnemyCityCount - centerResult.OwnedCityCount >= 2)
                {
                    Loader.modLogger?.LogInfo(
                    $"[Conquest-Evacuation] City at location ({tile.coordinates.X}, {tile.coordinates.Y}) is isolated. " +
                    $"(Enemies: {centerResult.EnemyCityCount}, Allies: {centerResult.OwnedCityCount}, Gap: {centerResult.EnemyCityCount - centerResult.OwnedCityCount}). " +
                    $"Forcing Evacuation selection!");

                    __result = EnumCache<CityReward>.GetType("evacuation");
                }
                else
                if (tile.unit != null && tile.unit.owner != playerState.Id)
                {
                    __result = EnumCache<CityReward>.GetType("evacuation");
                }
                else
                {
                    int num = random.Next(0, 2);
                    if (num == 0)
                    {
                        __result = CityReward.Explorer;
                    }
                    else
                    {
                        __result = CityReward.Workshop;
                    }
                }
            }
            else
            if (tile.improvement.level == 3)
            {
                int num = random.Next(0, 3);
                if (num == 0)
                {
                    __result = CityReward.CityWall;
                }
                else
                if (num == 1)
                {
                    __result = CityReward.Resources;
                } 
                else
                {
                    __result = EnumCache<CityReward>.GetType("valhalla");
                }
            }
            else
            if (tile.improvement.level == 4)
            {
                CityAnalysisResult? centerResult = MapAnalysis.ScanCity(gameState.Map, gameState, tile, 8, true, playerState);
                CityAnalysisResult? centerResult2 = MapAnalysis.ScanCity(gameState.Map, gameState, tile, 3, true, playerState);
                // MapAnalysis.LogAnalysisResult(tile, centerResult, 8);

                if (centerResult != null && centerResult.EnemyCityCount == 0 && playerState.cities >= 4)
                {
                    Loader.modLogger?.LogInfo(
                    $"[Conquest-Tax] City at location ({tile.coordinates.X}, {tile.coordinates.Y}) is protected. " +
                    $"(Enemies: {centerResult.EnemyCityCount}). " +
                    $"Forcing Tax Reform selection!");

                    __result = EnumCache<CityReward>.GetType("taxreform");
                }
                else
                if (centerResult != null && centerResult.OwnedCityCount >= 5)
                {
                    Loader.modLogger?.LogInfo(
                    $"[Conquest-Tax] Capital at location ({tile.coordinates.X}, {tile.coordinates.Y}) is protected. " +
                    $"(Enemies: {centerResult.EnemyCityCount}). " +
                    $"Forcing Tax Reform selection!");

                    __result = EnumCache<CityReward>.GetType("taxreform");

                }
                else
                {
                    int num = random.Next(0, 1);
                    if (num == 0)
                    {
                        __result = CityReward.BorderGrowth;
                    }
                    else
                    {
                        __result = CityReward.PopulationGrowth;
                    }
                }
            }
            else
            if (tile.improvement.level >= 5)
            {
                __result = CityReward.SuperUnit;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(AI), nameof(AI.GetImprovementScore))]
        private static void GetImprovementScore_Citadel(GameState gameState, ImprovementData improvementData, TileData tileData, PlayerState player, ref float __result)
        {
            if (gameState == null || tileData == null || player == null) return;
            if (!gameState.GameLogicData.IsUnlocked(improvementData.type, player) && improvementData.type != EnumCache<ImprovementData.Type>.GetType("citadel")) return;
            
            try
            {
                float num = 0;
                TileData rulingCity = gameState.Map.GetTile(tileData.rulingCityCoordinates);
                if (rulingCity != null && rulingCity.improvement != null)
                {
                    int expansionRadius = rulingCity.improvement.borderSize;

                    CityAnalysisResult? bestCornerResult = MapAnalysis.ScanCity(
                        gameState.Map,
                        gameState,
                        rulingCity,
                        5,
                        false, 
                        player,
                        Faction.Both,
                        false
                    );

                    if (bestCornerResult != null && bestCornerResult.TargetTile != null)
                    {
                        TileData targetedCornerTile = bestCornerResult.TargetTile;

                        if (tileData.coordinates.X == targetedCornerTile.coordinates.X && 
                            tileData.coordinates.Y == targetedCornerTile.coordinates.Y)
                        {
                            float totalStrategicScore = 0f;

                            WorldCoordinates centerCoord = new WorldCoordinates(tileData.coordinates.X, tileData.coordinates.Y);
                            TileData[] nearbyTiles = gameState.Map.GetAreaSorted(centerCoord, expansionRadius, true, true);

                            int unclaimedCount = 0;
                            if (nearbyTiles != null)
                            {
                                foreach (var tileInZone in nearbyTiles)
                                {
                                    if (tileInZone != null && (tileInZone.owner == 0))
                                    {
                                        unclaimedCount++;
                                    }
                                }
                            }

                            // MapAnalysis.LogAnalysisResult(rulingCity, bestCornerResult, 5);

                            if (unclaimedCount > 0)
                            {
                                float expansionScore = unclaimedCount * 80f; 
                                totalStrategicScore += expansionScore;
                                
                                /*Loader.modLogger?.LogInfo(
                                    $"[AI-Expansion] Strategic Corner Matched! Corner {bestCornerResult.TileTypeLabel} can successfully claim {unclaimedCount} tiles. " +
                                    $"Adding Expansion Score: +{expansionScore}");*/
                            }

                            if (bestCornerResult.EnemyCityCount > 0 || bestCornerResult.OwnedCityCount > 0)
                            {
                                float militaryScore = bestCornerResult.EnemyCityCount * 20f - bestCornerResult.OwnedCityCount * 10f; 
                                totalStrategicScore += militaryScore;

                                /*Loader.modLogger?.LogInfo(
                                    $"[AI-Tactics] Frontline Warning! Corner {bestCornerResult.TileTypeLabel} detected {bestCornerResult.EnemyCityCount} enemies within radius 5. " +
                                    $"Adding Military Score: +{militaryScore}");*/
                            }
                            num += totalStrategicScore;
                        }
                        else
                        {
                            num *= 0.1f;
                        }
                    }
                }
                num *= AI.getPriceFactor(improvementData.cost, player);
                __result = num;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-AI] Error in GetImprovementScore: {ex}");
            }
        }           
                    
        // =========================================================================
        // C. Military Behaviors
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(AI), nameof(AI.GetBuildUnitScore))]
        private static void GetBuildUnitScore_DenyStiff(GameState gameState, PlayerState player, UnitData unit, AI.UnitStats desiredUnitStats, ref float __result, TileData? tile = null)
        {
            try
            {
                if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return;
                }

                if (!unit.HasAbility(UnitAbility.Type.Stiff)) return;

                if (tile != null && !IsTileSafe(gameState, player, tile))
                {
                    __result = 0;
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-AI] Error in GetBuildUnitScore: {ex}");
            }
        }

        /*[HarmonyPostfix]
        [HarmonyPatch(typeof(PathFinder), nameof(PathFinder.GetMoveOptions))]
        private static void GetMoveOptions_StopSuicide(GameState gameState, WorldCoordinates start, int maxCost, UnitState unit, ref Il2CppSystem.Collections.Generic.List<WorldCoordinates> __result)
        {
            try
            {
                if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return;
                }

                if (!unit.HasAbility(UnitAbility.Type.Stiff)) return;

                PlayerState player;
                gameState.TryGetPlayer(unit.owner, out player);

                // 1. Create a safe, temporary C# array snapshot of the results
                var optionsCopy = __result.ToArray();
                // Loader.modLogger?.LogError($"[Conquest-AI] Success ToArray");

                // 2. Iterate over the safe snapshot instead
                foreach (WorldCoordinates option in optionsCopy)
                {
                    TileData tile = gameState.Map.GetTile(option);
                    if (!IsTileSafe(gameState, player, tile))
                    {
                        // 3. It is now perfectly safe to remove from the original __result list
                        // Loader.modLogger?.LogError($"[Conquest-AI] Tryna remove");
                        __result.Remove(option);
                    }
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-AI] Error in GetMoveOptions: {ex}");
            }
        }*/

        public static bool IsTileSafe(GameState gameState, PlayerState player, TileData tile)
        {
            TileData[] areaSorted = MapDataExtensions.GetAreaSorted(gameState.Map, tile.coordinates, 6, true, true);
            foreach (TileData tile2 in areaSorted) {
                if (tile2.unit != null && tile2.unit.owner != player.Id)
                {
                    TileData[] areaSorted2 = MapDataExtensions.GetAreaSorted(gameState.Map, tile2.coordinates, 2, true, true);
                    Il2CppSystem.Collections.Generic.List<WorldCoordinates> moveOptions = tile2.unit.GetMovementOptions(gameState, UnitDataExtensions.GetMovement(tile2.unit, gameState));
                    Il2CppSystem.Collections.Generic.List<WorldCoordinates> attackOptions = tile2.unit.GetAttackOptions(gameState, UnitDataExtensions.GetRange(tile2.unit.UnitData));
                    
                    foreach (TileData tile3 in areaSorted2) {
                        {
                            if (moveOptions.Contains(tile3.coordinates) && tile2.unit.HasAbility(UnitAbility.Type.Dash))
                            {
                                return false;
                            }
                        }
                    }

                    if (attackOptions.Contains(tile.coordinates))
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }
}