using HarmonyLib;
using Il2CppInterop.Runtime.Runtime;
using Il2CppSystem.Xml.Schema;
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
                                float militaryScore = bestCornerResult.EnemyCityCount * 50f - bestCornerResult.OwnedCityCount * 20f; 
                                totalStrategicScore += militaryScore;

                                /*Loader.modLogger?.LogInfo(
                                    $"[AI-Tactics] Frontline Warning! Corner {bestCornerResult.TileTypeLabel} detected {bestCornerResult.EnemyCityCount} enemies within radius 5. " +
                                    $"Adding Military Score: +{militaryScore}");*/
                            }
                            num += totalStrategicScore;
                            if (rulingCity.improvement.borderSize == 2)
                            {
                                num *= 0.5f;
                                /*if (num >= 180 && targetedCornerTile.improvement != null && !targetedCornerTile.improvement.type.IsMonument())
                                {
                                    gameState.ActionStack.Add(new DestroyImprovementAction(player.Id, targetedCornerTile.coordinates));
                                }*/
                            }
                            else
                            if (rulingCity.improvement.borderSize == 3)
                            {
                                /*if (num >= 900 && targetedCornerTile.improvement != null && !targetedCornerTile.improvement.type.IsMonument())
                                {
                                    gameState.ActionStack.Add(new DestroyImprovementAction(player.Id, targetedCornerTile.coordinates));
                                }*/
                            }
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

        /*[HarmonyPostfix]
        [HarmonyPatch(typeof(AI), nameof(AI.AddPossibleImprovementCommands))]
        private static void AddPossibleImprovementCommands_Citadel(GameState gameState, PlayerState player, List<AI.ScoredCommand> possibleCommands)
        {
            try
            {
                foreach (TileData tileData in player.aiState.PlayerMapData.empireTiles)
                {
                    if (tileData.improvement != null)
                    {
                        //ImprovementData previousData;
                        //gameState.GameLogicData.TryGetData(tileData.improvement.type, out previousData);
                        //float num = AI.GetImprovementScore(gameState, previousData, tileData, player);
                        //Loader.modLogger?.LogInfo($"[Conquest-AI] Old improvement is {tileData.improvement.name} with {num}.");

                        ImprovementData citadelData;
                        gameState.GameLogicData.TryGetData(EnumCache<ImprovementData.Type>.GetType("citadel"), out citadelData);
                        float num2 = AI.GetImprovementScore(gameState, citadelData, tileData, player);
                
                        TileData[] nearbyTiles = gameState.Map.GetAreaSorted(tileData.coordinates, 3, true, true);

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

                        Loader.modLogger?.LogInfo($"[Conquest-AI] Citadel improvement is Citadel with {unclaimedCount}.");

                        if (unclaimedCount > 4)
                        {
                            CommandBase command = new DestroyCommand(player.Id, tileData.coordinates);
                            possibleCommands.Add(new AI.ScoredCommand
                            {
                                command = command,
                                score = 1000
                            });
                            Loader.modLogger?.LogInfo($"[Conquest-AI] Overrided old improvement when {unclaimedCount}.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-AI] Error in AddPossibleImprovementCommands: {ex}");
            }
        }*/    

        private static int lastProcessedTurn = -1;
        private static byte lastProcessedPlayer = 255;
        private static readonly HashSet<WorldCoordinates> processedTilesThisTurn = new HashSet<WorldCoordinates>();

        [HarmonyPostfix]
        [HarmonyPatch(typeof(AI), nameof(AI.GetTileCommands))]
        private static void GetTileCommands_DestroyCmd(GameState gameState, PlayerState player, CommandType specificCommand, ref CommandBase __result)
        {
            try
            {
                if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return;
                }

                if (__result == null) return;

                var empireTiles = player.aiState?.PlayerMapData?.empireTiles;
                if (empireTiles == null) return;

                // 【安全自動解鎖機制】：如果發現遊戲回合變了，或者換別的國家 AI 思考了，自動清空鎖
                if (gameState.CurrentTurn != lastProcessedTurn || player.Id != lastProcessedPlayer)
                {
                    lastProcessedTurn = (int)gameState.CurrentTurn;
                    lastProcessedPlayer = player.Id;
                    processedTilesThisTurn.Clear();
                }

                // =========================================================================
                // 【核心效能優化】：高效率城市 Citadel 角落預先快取 (Pre-caching)
                // 用來儲存：[城市座標 -> 該城市算出來的最優 Citadel 邊角格子]
                // =========================================================================
                var cityCitadelCornerCache = new Dictionary<WorldCoordinates, TileData>();
                
                foreach (TileData city in player.GetCityTiles(gameState))
                {
                    if (city != null)
                    {
                        // 每個城市僅在最外層跑「唯一一次」沉重的全圖掃描，徹底斬斷重複計算
                        CityAnalysisResult? bestCornerResult = ForceScanCornerForCitadel(
                            gameState.Map, gameState, city, 5, false, player, Faction.Both, false
                        );
                        
                        if (bestCornerResult != null && bestCornerResult.TargetTile != null)
                        {
                            cityCitadelCornerCache[city.coordinates] = bestCornerResult.TargetTile;
                        }
                    }
                }

                float globalBestScoreDifference = 0f; 
                DestroyCommand? globalBestDestroyCmd = null;
                
                ImprovementData? bestOldType = null;
                ImprovementData? bestNewType = null;

                string globalOldName = "";
                string globalNewName = "";
                float globalOldScore = 0f;
                float globalNewScore = 0f;

                foreach (TileData tileData in empireTiles)
                {  
                    if (tileData != null && tileData.improvement != null && 
                        tileData.improvement.type != ImprovementData.Type.City && 
                        tileData.improvement.type != ImprovementData.Type.LightHouse && 
                        tileData.improvement.type != EnumCache<ImprovementData.Type>.GetType("citadel"))
                    {
                        if (processedTilesThisTurn.Contains(tileData.coordinates))
                        {
                            continue;
                        }

                        ImprovementData previousData;
                        if (!gameState.GameLogicData.TryGetData(tileData.improvement.type, out previousData)) continue;

                        float num = ForceGetImprovementScore(gameState, previousData, tileData, player);
                        num = (float)Math.Round(num);
                        string name = previousData.type.GetDisplayName();
                        //Loader.modLogger?.LogInfo($"[Conquest-AI] Old previous improvement is {name} with {num}.");

                        foreach (CommandBase commandBase in ForceGetBuildableImprovements(gameState, player, tileData, true))
                        {
                            BuildCommand buildCommand = commandBase.Cast<BuildCommand>();
                            ImprovementData currentData;
                            if (!gameState.GameLogicData.TryGetData(buildCommand.Type, out currentData)) continue;

                            float num2 = ForceGetImprovementScore(gameState, currentData, tileData, player);
                            string name2 = currentData.type.GetDisplayName();

                            if (currentData.type == EnumCache<ImprovementData.Type>.GetType("citadel"))
                            {
                                TileData capital = gameState.Map.GetTile(tileData.rulingCityCoordinates);
                                
                                // 【高效優化點】：不再呼叫 ForceScanCornerForCitadel！
                                // 直接從剛才預算好的 Dictionary 快取中讀取結果，耗時從幾十毫秒瞬間縮短到 0 毫秒！
                                if (cityCitadelCornerCache.TryGetValue(tileData.rulingCityCoordinates, out TileData? targetedCornerTile) && targetedCornerTile != null)
                                {
                                    int unclaimedCount = 0;

                                    if (tileData.coordinates.X == targetedCornerTile.coordinates.X && 
                                        tileData.coordinates.Y == targetedCornerTile.coordinates.Y)
                                    {
                                        TileData[] nearbyTiles = MapDataExtensions.GetAreaSorted(gameState.Map, tileData.coordinates, capital.improvement.borderSize, true, true);
                                        if (nearbyTiles != null)
                                        {
                                            for (int i = 0; i < nearbyTiles.Length; i++)
                                            {
                                                if (nearbyTiles[i] != null && nearbyTiles[i].owner == 0)
                                                {
                                                    unclaimedCount++;
                                                }
                                            }
                                        }
                                    }
                                    num2 = (float)(unclaimedCount * 75 / Math.Pow(capital.improvement.borderSize, 1.5));
                                }
                                else
                                {
                                    // 如果這個城市在預算階段就找不到合適的邊角，說明此格子不適合蓋 Citadel
                                    continue; 
                                }
                            }

                            if ((num2 > 0f && currentData.rewards.GetPopulation() > 0) || currentData.growthRewards.GetPopulation() > 0)
                            {
                                TileData tile = gameState.Map.GetTile(tileData.rulingCityCoordinates);
                                if (tile.CanCityBeUpgraded(gameState))
                                {
                                    int num3 = tile.PopulationNeededToUpgradeCity();
                                    if (num3 > 0)
                                    {
                                        num2 += (float)(200 / num3);
                                    }
                                }
                            }

                            num2 *= AI.getPriceFactor(currentData.cost, player);
                            num2 = (float)Math.Round(num2);
                            //Loader.modLogger?.LogInfo($"[Conquest-AI] New possible improvement is {name2} with {num2}.");

                            if (num2 > num && num2 > 150
                                && !previousData.type.IsMonument() && !previousData.type.IsTemple()
                                && !currentData.type.IsMonument() && !currentData.type.IsTemple())
                            {
                                float scoreDifference = num2 - num;

                                 if (scoreDifference > globalBestScoreDifference)
                                {
                                    DestroyCommand destroyCmd = new DestroyCommand(player.Id, tileData.coordinates);
                                    if (destroyCmd.IsValid(gameState))
                                    {
                                        globalBestScoreDifference = scoreDifference;
                                        globalBestDestroyCmd = destroyCmd;
                                        
                                        bestOldType = previousData;
                                        bestNewType = currentData;

                                        globalOldName = name;
                                        globalNewName = name2;
                                        globalOldScore = num;
                                        globalNewScore = num2;
                                    }
                                }
                            }
                        }
                    }
                }

                if (globalBestDestroyCmd != null)
                {
                    TileData cmdTile = gameState.Map.GetTile(globalBestDestroyCmd.Coordinates);

                    if (bestOldType?.type == bestNewType?.type)
                    {
                        //Loader.modLogger?.LogInfo($"[Conquest-AI] Best replacement for {globalOldName} at {globalBestDestroyCmd.Coordinates} is the same improvement. Destroy command safely denied.");
                    }
                    else if (bestNewType != null && bestNewType.HasAbility(ImprovementAbility.Type.Consumed))
                    {
                        //Loader.modLogger?.LogInfo($"[Conquest-AI] Best replacement for {globalOldName} at {globalBestDestroyCmd.Coordinates} is a resource ({globalNewName}). Destroy command safely denied.");
                    }
                    else if (bestOldType?.type == ImprovementData.Type.Bridge)
                    {
                        //Loader.modLogger?.LogInfo($"[Conquest-AI] Trying to replace {globalOldName} at {globalBestDestroyCmd.Coordinates}. Destroy command safely denied.");
                    }
                    else if (!(cmdTile.unit != null && cmdTile.unit.owner != cmdTile.owner))
                    {
                        processedTilesThisTurn.Add(globalBestDestroyCmd.Coordinates);

                        __result = globalBestDestroyCmd;
                        //Loader.modLogger?.LogInfo($"[Conquest-AI] Globally selected best conversion at {globalBestDestroyCmd.Coordinates}: Destroying {globalOldName} ({globalOldScore}) to clear path for {globalNewName} ({globalNewScore}) [Net Gain: +{globalBestScoreDifference}].");
                    }
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-AI] Error in GetTileCommands_DestroyCmd: {ex}");
            }
        }
                    
        // =========================================================================
        // C. Military Behaviors
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(AI), nameof(AI.GetBuildUnitScore))]
        private static void GetBuildUnitScore_Combined(GameState gameState, PlayerState player, UnitData unit, AI.UnitStats desiredUnitStats, ref float __result, TileData? tile = null)
        {
            try
            {
                if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return;
                }
                if (!player.AutoPlay) return;

                if (tile == null) return;

                // 1. Deny Stiff
                if (unit.HasAbility(UnitAbility.Type.Stiff))
                {
                    if (IsTileDangerous(gameState, player, tile.coordinates, 6))
                    {
                        //Loader.modLogger?.LogInfo($"[Conquest-AI] Denied stiff at {tile.coordinates}.");
                        __result = 0f;
                        return;
                    }
                }

                // 2. Deny Closed Lake
                if (tile.IsWater)
                {
                    var empireTiles = player.aiState?.PlayerMapData?.empireTiles;
                    if (empireTiles != null && empireTiles.Contains(tile))
                    {
                        if (IsWaterUnderMyControl(gameState, tile, player))
                        {
                            if (unit.type == UnitData.Type.Rammership)
                            {
                                //Loader.modLogger?.LogInfo($"[Conquest-AI] Zero score for Rammer in closed lake at {tile.coordinates}.");
                                __result = 0f;
                            }
                            else if (unit.type == UnitData.Type.Bombership || unit.type == UnitData.Type.Scout)
                            {
                                //Loader.modLogger?.LogInfo($"[Conquest-AI] Reduced score for Bomber/Scout in closed lake at {tile.coordinates}.");
                                __result *= 0.5f;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-AI] Error in GetBuildUnitScore Combined: {ex}");
            }
        }

        private static bool IsWaterUnderMyControl(GameState gameState, TileData startTile, PlayerState player)
        {
            // 如果起點根本不是水域，就談不上掌控，返回 false
            if (!startTile.IsWater)
            {
                return false;
            }

            var queue = new Queue<WorldCoordinates>();
            var visited = new HashSet<WorldCoordinates>();

            queue.Enqueue(startTile.coordinates);
            visited.Add(startTile.coordinates);

            int maxIterations = 3000;
            int iterations = 0;

            while (queue.Count > 0 && iterations < maxIterations)
            {
                iterations++;
                WorldCoordinates currentCoord = queue.Dequeue();
                TileData currentTile = gameState.Map.GetTile(currentCoord);

                if (currentTile == null) continue;

                TileData[] neighbors = MapDataExtensions.GetAreaSorted(gameState.Map, currentCoord, 1, true, false);
                for (int i = 0; i < neighbors.Length; i++)
                {
                    TileData neighbor = neighbors[i];
                    if (neighbor == null) continue;

                    if (neighbor.improvement != null && neighbor.improvement.type == ImprovementData.Type.City)
                    {
                        if (neighbor.owner != player.Id && neighbor.owner != 0)
                        {
                            return false;
                        }
                    }

                    if (neighbor.IsWater && !visited.Contains(neighbor.coordinates))
                    {
                        visited.Add(neighbor.coordinates);
                        queue.Enqueue(neighbor.coordinates);
                    }
                }
            }

            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PathFinder), nameof(PathFinder.GetMoveOptions))]
        private static void GetMoveOptions_Combined(GameState gameState, WorldCoordinates start, int maxCost, UnitState unit, ref Il2CppSystem.Collections.Generic.List<WorldCoordinates> __result)
        {
            try
            {
                if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return;
                }

                PlayerState player;
                if (!gameState.TryGetPlayer(unit.owner, out player)) return;
                if (!player.AutoPlay) return;

                var dangerousTiles = GetDangerousTilesInArea(gameState, player, start, maxCost + 6);

                var optionsCopy = __result.ToArray();
                foreach (WorldCoordinates option in optionsCopy)
                {
                    TileData origin = gameState.Map.GetTile(start);
                    TileData target = gameState.Map.GetTile(option);

                    // 1. Stop Suicide
                    if (unit.UnitData.HasAbility(UnitAbility.Type.Stiff) && unit.type != UnitData.Type.Juggernaut && !unit.HasAbility(UnitAbility.Type.Infiltrate))
                    {
                        if (dangerousTiles.Contains(option))
                        {
                            __result.Remove(option);
                        }

                        if (!target.terrain.IsWater() && unit.type == UnitData.Type.Bombership)
                        {
                            __result.Remove(option);
                        }
                    }
                    
                    // 2. Stop playing water
                    if (unit.UnitData.HasAbility(UnitAbility.Type.Carry) && unit.type != UnitData.Type.Bombership)
                    {
                        if (origin.terrain.IsWater() && target.terrain.IsWater() && IsWaterUnderMyControl(gameState, origin, player))
                        {
                            __result.Remove(option);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-AI] Error in GetMoveOptions: {ex}");
            }
        }

        private static HashSet<WorldCoordinates> GetDangerousTilesInArea(GameState gameState, PlayerState player, WorldCoordinates center, int radius)
        {
            var dangerousTiles = new HashSet<WorldCoordinates>();
            TileData[] nearbyTiles = MapDataExtensions.GetAreaSorted(gameState.Map, center, radius, true, true);
            
            for (int i = 0; i < nearbyTiles.Length; i++)
            {
                TileData enemyTile = nearbyTiles[i];
                if (enemyTile.unit != null && enemyTile.unit.owner != player.Id)
                {
                    UnitState enemy = enemyTile.unit;
                    
                    int movementRange = UnitDataExtensions.GetMovement(enemy, gameState);
                    int attackRange = UnitDataExtensions.GetRange(enemy.UnitData);
                    int totalThreatRadius = 0;

                    if (enemy.HasAbility(UnitAbility.Type.Dash))
                    {
                        totalThreatRadius = movementRange + attackRange;
                    }
                    else
                    {
                        totalThreatRadius = Math.Max(movementRange, attackRange);
                    }

                    TileData[] threatArea = MapDataExtensions.GetAreaSorted(gameState.Map, enemyTile.coordinates, totalThreatRadius, true, true);
                    for (int j = 0; j < threatArea.Length; j++)
                    {
                        dangerousTiles.Add(threatArea[j].coordinates);
                    }
                }
            }
            return dangerousTiles;
        }

        private static bool IsTileDangerous(GameState gameState, PlayerState player, WorldCoordinates targetCoord, int scanRadius)
        {
            TileData[] nearbyTiles = MapDataExtensions.GetAreaSorted(gameState.Map, targetCoord, scanRadius, true, true);
            
            for (int i = 0; i < nearbyTiles.Length; i++)
            {
                TileData enemyTile = nearbyTiles[i];
                if (enemyTile.unit != null && enemyTile.unit.owner != player.Id)
                {
                    UnitState enemy = enemyTile.unit;
                    
                    int movementRange = UnitDataExtensions.GetMovement(enemy, gameState);
                    int attackRange = UnitDataExtensions.GetRange(enemy.UnitData);
                    int totalThreatRadius = 0;

                    if (enemy.HasAbility(UnitAbility.Type.Dash))
                    {
                        totalThreatRadius = movementRange + attackRange;
                    }
                    else
                    {
                        totalThreatRadius = Math.Max(movementRange, attackRange);
                    }

                    int distance = Math.Max(Math.Abs(enemyTile.coordinates.x - targetCoord.x), Math.Abs(enemyTile.coordinates.y - targetCoord.y));
                    if (distance <= totalThreatRadius)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // =========================================================================
        // D. Forced Get Methods
        // =========================================================================
        private static Il2CppSystem.Collections.Generic.List<CommandBase> ForceGetBuildableImprovements(GameState gameState, PlayerState player, TileData tile, bool includeUnavailable = false)
        {		
            Il2CppSystem.Collections.Generic.List<CommandBase> list = new Il2CppSystem.Collections.Generic.List<CommandBase>();
            if (player.Id != gameState.CurrentPlayer)
            {
                return list;
            }

            GameLogicData data = new GameLogicData();
            foreach (ImprovementData improvementData in gameState.GameLogicData.GetUnlockedImprovements(player))
            {
                if (!improvementData.HasAbility(ImprovementAbility.Type.Manual)
                    //&& improvementData.type != tile.improvement.type
                    && player.currency >= improvementData.cost
                    && data.MeetsRequirement(tile, improvementData, player, gameState)
                    && data.MeetsAdjacencyRequirement(gameState.Map, tile, improvementData.adjacencyRequirements))
                {
                    CommandBase commandBase = new BuildCommand(player.Id, improvementData.type, tile.coordinates);
                    if (includeUnavailable || commandBase.IsValid(gameState))
                    {
                        list.Add(commandBase);
                    }
                }
            }
            return list;
        }

        private static float ForceGetImprovementScore(GameState gameState, ImprovementData improvementData, TileData tileData, PlayerState player)
        {
            float num = 0f;
            int population = improvementData.rewards.GetPopulation();
            num += (float)(population * 25);
            num += (float)(improvementData.rewards.GetCurrency() * 2);
            num += (float)(improvementData.work * 20);
            if (improvementData.HasAbility(ImprovementAbility.Type.Patina))
            {
                population = improvementData.growthRewards.GetPopulation();
                num += (float)(population * 100);
            }
            UnitData unit = improvementData.creates.GetUnit();
            if (unit != null)
            {
                AI.UnitStats desiredUnitStats = AI.GetDesiredUnitStats(gameState, player, tileData);
                num += AI.GetBuildUnitScore(gameState, player, unit, desiredUnitStats, tileData);
            }
            if (improvementData.adjacencyImprovements != null && improvementData.adjacencyImprovements.Count > 0)
            {
                int num2 = ActionUtils.GetAdjacencyBonusAt(gameState, tileData, improvementData);
                if (improvementData.adjacencyImprovements.Contains(ImprovementData.Type.PolarisClimate))
                {
                    num2 += player.aiState.frozenTileCount / 20;
                }
                num += (float)(improvementData.growthRewards.GetPopulation() * 20 * num2);
                num += (float)(improvementData.work * 20 * num2);
            }
            if (tileData.rulingCityCoordinates != WorldCoordinates.NULL_COORDINATES)
            {
                TileData tile = gameState.Map.GetTile(tileData.rulingCityCoordinates);
                ImprovementState improvement = tile.improvement;
                if (improvement != null && improvement.type == ImprovementData.Type.City && (int)improvement.xp + population > (int)improvement.level)
                {
                    num += 100f;
                }
                if (improvementData.IsRouteOpener())
                {
                    bool flag = false;
                    foreach (TileData tileData2 in gameState.Map.GetArea(tile.coordinates, 1, true, false))
                    {
                        ImprovementData data;
                        if (tileData2.improvement != null && gameState.GameLogicData.TryGetData(tileData2.improvement.type, out data) && data.IsRouteOpener())
                        {
                            flag = true;
                            break;
                        }
                    }
                    if (!tile.IsConnected && !flag)
                    {
                        num += 30f;
                        if (tile.coordinates == player.startTile)
                        {
                            num += 50f;
                        }
                    }
                }
            }
            if (gameState.Settings.RulesGameMode == (GameMode)1)
            {
                int score = improvementData.rewards.GetScore();
                num += (float)score / 10f;
            }
            num += AI.GetImprovementAbilityScore(gameState, player, improvementData, tileData);
            if (improvementData.type != ImprovementData.Type.Road && tileData.resource != null && !gameState.GameLogicData.IsResourceRequiredByImprovement(tileData.resource.type, improvementData))
            {
                num *= 0.5f;
            }
            return num;
        }

        public static CityAnalysisResult? ForceScanCornerForCitadel(
            MapData map, 
            GameState gameState,
            TileData cityTile,
            int searchRadius, 
            bool searchFromCenter,
            PlayerState currentOwner,
            Faction findType = Faction.Both, // Default when searchFromCenter
            bool findMost = true)            // Default when searchFromCenter
        {
            if (gameState == null || cityTile == null || map == null || currentOwner == null) return null;

            Il2CppSystem.Collections.Generic.List<TileData> territoryTiles = ActionUtils.GetCityAreaSorted(gameState, cityTile);
            if (territoryTiles == null || territoryTiles.Count == 0) return null;

            var startingPoints = new System.Collections.Generic.Dictionary<string, TileData>();

            if (searchFromCenter)
            {
                startingPoints.Add("CityCenter", cityTile);
            }
            else
            {
                TileData? topLeft = null;
                TileData? topRight = null;
                TileData? bottomLeft = null;
                TileData? bottomRight = null;

                float maxTR = float.MinValue, minBL = float.MaxValue;
                float maxBR = float.MinValue, minTL = float.MaxValue;

                foreach (var tile in territoryTiles)
                {
                    if (tile == null) continue;
                    int x = tile.coordinates.X; 
                    int y = tile.coordinates.Y;

                    float sum = x + y;
                    if (sum > maxTR) { maxTR = sum; topRight = tile; }
                    if (sum < minBL) { minBL = sum; bottomLeft = tile; }

                    float diff = x - y;
                    if (diff > maxBR) { maxBR = diff; bottomRight = tile; }
                    if (diff < minTL) { minTL = diff; topLeft = tile; }
                }

                if (topLeft != null && MapDataExtensions.DistanceToEdge(map, topLeft.coordinates) != 0) startingPoints["TopLeft"] = topLeft;
                if (topRight != null && MapDataExtensions.DistanceToEdge(map, topRight.coordinates) != 0) startingPoints["TopRight"] = topRight;
                if (bottomLeft != null && MapDataExtensions.DistanceToEdge(map, bottomLeft.coordinates) != 0) startingPoints["BottomLeft"] = bottomLeft;
                if (bottomRight != null && MapDataExtensions.DistanceToEdge(map, bottomRight.coordinates) != 0) startingPoints["BottomRight"] = bottomRight;
            }

            CityAnalysisResult? bestResult = null;

            foreach (var kvp in startingPoints)
            {
                string label = kvp.Key;
                TileData startTile = kvp.Value;

                if (startTile == null) continue;

                /*bool canAccess = false;
                Il2CppSystem.Collections.Generic.List<TerrainData> unlockedMovements = gameState.GameLogicData.GetUnlockedMovements(currentOwner);
                foreach (var accessible in unlockedMovements)
                {
                    if (startTile.terrain == accessible.type)
                    {
                        canAccess = true;
                        break;
                    }
                }
                if (!canAccess) continue;*/

                WorldCoordinates centerCoord = new WorldCoordinates(startTile.coordinates.X, startTile.coordinates.Y);
                TileData[] areaTiles = map.GetAreaSorted(centerCoord, searchRadius, true, true);

                int enemyCityCount = 0;
                int ownedCityCount = 0; 

                if (areaTiles != null)
                {
                    foreach (var areaTile in areaTiles)
                    {
                        if (areaTile == null || areaTile.improvement == null) continue;

                        if (areaTile.improvement.type == ImprovementData.Type.City)
                        {
                            if (areaTile.coordinates.X == cityTile.coordinates.X && 
                                areaTile.coordinates.Y == cityTile.coordinates.Y)
                            {
                                continue;
                            }

                            if (areaTile.owner != currentOwner.Id) 
                            {
                                enemyCityCount++; 
                            }
                            else
                            {
                                ownedCityCount++; 
                            }
                        }
                    }
                }

                var currentResult = new CityAnalysisResult 
                { 
                    TargetTile = startTile, 
                    EnemyCityCount = enemyCityCount,
                    OwnedCityCount = ownedCityCount,
                    TileTypeLabel = label
                };

                if (bestResult == null)
                {
                    bestResult = currentResult;
                }
                else
                {
                    int currentCount = findType switch
                    {
                        Faction.Enemy => currentResult.EnemyCityCount,
                        Faction.Owned => currentResult.OwnedCityCount,
                        Faction.Both  => currentResult.EnemyCityCount + currentResult.OwnedCityCount,
                        _             => 0
                    };

                    int bestCount = findType switch
                    {
                        Faction.Enemy => bestResult.EnemyCityCount,
                        Faction.Owned => bestResult.OwnedCityCount,
                        Faction.Both  => bestResult.EnemyCityCount + bestResult.OwnedCityCount,
                        _             => 0
                    };

                    if (findMost)
                    {
                        if (currentCount > bestCount) bestResult = currentResult;
                    }
                    else
                    {
                        if (currentCount < bestCount) bestResult = currentResult;
                    }
                }
            }
            return bestResult;
        }
    }
}