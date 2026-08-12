using HarmonyLib;
using Polytopia.Data;
using PolytopiaBackendBase.Game;

namespace PolyMode
{
    public static class AI_2
    {
        // =========================================================================
        // Caches
        // =========================================================================
        public static int lastProcessedTurn = -1;
        public static byte lastProcessedPlayer = 255;
        public static readonly HashSet<WorldCoordinates> processedTilesThisTurn = new HashSet<WorldCoordinates>();

        public static readonly Dictionary<WorldCoordinates, TileData> cityCitadelCornerCache =
            new Dictionary<WorldCoordinates, TileData>();
        public static int citadelCacheTurn = -1;

        public static readonly Dictionary<byte, HashSet<WorldCoordinates>> dangerousTilesCache =
            new Dictionary<byte, HashSet<WorldCoordinates>>();
        public static int dangerousCacheTurn = -1;
        public static bool skipMoveOptionsPatch = false;

        // =========================================================================
        // A. Diplomacy
        // =========================================================================
        [HarmonyPrefix]
        [HarmonyPatch(typeof(AI), nameof(AI.GetGameProgress))]
        private static bool GetGameProgress_Conquest(
            ref float __result, GameState gameState, PlayerState winningPlayer)
        {
            if (gameState?.Settings == null) return true;
            try
            {
                if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                    return true;

                if (winningPlayer == null)
                {
                    __result = 0f;
                    return false;
                }

                float totalCities = Math.Max(0.1f, MapDataExtensions.CountCities(gameState));
                float cityProgress = winningPlayer.cities / totalCities;
                __result = Math.Min(1f, Math.Max(0f, cityProgress));
                return false;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-AI] GetGameProgress: {ex.Message}");
                __result = 0f;
                return false;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(OpinionManager), nameof(OpinionManager.UpdateOpinion))]
        private static void UpdateOpinion_Cities(
            OpinionManager __instance, GameState gameState, PlayerState player, PlayerState opponent)
        {
            if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                return;

            if (player == opponent || player.Id == 255 || opponent.Id == 255 || !opponent.IsAlive(gameState))
                return;

            if (player.GetRelation(opponent.Id).FirstMeet < 0
                && opponent.GetRelation(player.Id).LastAttackTurn < 0)
                return;

            float cityAdvantage = opponent.GetCityAdvantage(gameState);
            if (cityAdvantage <= 0f) return;

            float hate = cityAdvantage;
            var opinionState = new OpinionState();
            opinionState.AddOpinion(hate * 2.3f, EnumCache<OpinionManager.Type>.GetType("obstinate"));
            opinionState.AddOpinion(hate * 1.2f, OpinionManager.Type.Winning);

            if (!__instance.Opinions.ContainsKey(opponent.Id))
                __instance.Opinions[opponent.Id] = new OpinionState();

            __instance.Opinions[opponent.Id].AddOpinion(
                opinionState.GetOpinion(OpinionManager.Type.Winning) * -1f,
                OpinionManager.Type.Winning);
            __instance.Opinions[opponent.Id].AddOpinion(
                opinionState.GetOpinion(EnumCache<OpinionManager.Type>.GetType("obstinate")) * -1f,
                EnumCache<OpinionManager.Type>.GetType("obstinate"));
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(AI), nameof(AI.RateBattle))]
        private static void RateBattle_Cities(
            GameState gameState, UnitState attackingUnit, TileData defendingTile, ref float __result)
        {
            if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                return;

            if (defendingTile.owner == 0) return;

            float cityAdv = gameState.PlayerStates[defendingTile.owner].GetCityAdvantage(gameState);
            if (cityAdv > 0f)
                __result += cityAdv * 2f;
        }

        public static float GetAverageCities(GameState state)
        {
            int total = 0;
            int count = 0;
            foreach (PlayerState player in state.PlayerStates)
            {
                if (player.Id == 255) continue;
                total += player.IsAlive(state) ? player.CountCities(state) : 0;
                count++;
            }
            return count > 0 ? (float)total / count : 0f;
        }

        public static float GetCityAdvantage(this PlayerState player, GameState state)
        {
            return player.CountCities(state) - GetAverageCities(state);
        }

        // =========================================================================
        // B. Development
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(AI), nameof(AI.ChooseCityReward))]
        private static void ChooseCityReward_Conquest(
            GameState gameState, TileData tile, CityReward[] rewards, ref CityReward __result)
        {
            if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                return;

            if (!gameState.TryGetPlayer(tile.owner, out PlayerState playerState)
                || !gameState.GameLogicData.TryGetData(playerState.tribe, out TribeData _))
                return;

            var random = new Random();

            if (tile.improvement.level == 2)
            {
                var centerResult = MapAnalysis.ScanCityFromCenter(
                    gameState.Map, gameState, tile, 3, playerState);

                if (centerResult != null
                    && centerResult.EnemyCityCount >= 2
                    && centerResult.EnemyCityCount - centerResult.OwnedCityCount >= 2)
                {
                    __result = EnumCache<CityReward>.GetType("evacuation");
                }
                else if (tile.unit != null && tile.unit.owner != playerState.Id)
                {
                    __result = EnumCache<CityReward>.GetType("evacuation");
                }
                else
                {
                    __result = random.Next(0, 2) == 0 ? CityReward.Explorer : CityReward.Workshop;
                }
            }
            else if (tile.improvement.level == 3)
            {
                int roll = random.Next(0, 3);
                if (roll == 0) 
                {
                    __result = CityReward.CityWall;
                }
                else
                if (roll == 1) 
                {
                    __result = CityReward.Resources;
                }
                else 
                {
                    __result = EnumCache<CityReward>.GetType("valhalla");
                }
            }
            else if (tile.improvement.level == 4)
            {
                var centerResult = MapAnalysis.ScanCityFromCenter(
                    gameState.Map, gameState, tile, 8, playerState);

                if (centerResult != null && centerResult.EnemyCityCount == 0 && playerState.cities >= 4)
                {   
                    __result = EnumCache<CityReward>.GetType("taxreform");
                }
                else
                if (centerResult != null && centerResult.OwnedCityCount >= 5)
                {
                    __result = EnumCache<CityReward>.GetType("taxreform");
                }
                else
                {  
                    /*__result = random.Next(0, 1) == 0
                        ? CityReward.BorderGrowth
                        : CityReward.PopulationGrowth;*/
                    __result = CityReward.BorderGrowth;
                }
            }
            else if (tile.improvement.level >= 5)
            {
                __result = CityReward.SuperUnit;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(AI), nameof(AI.GetImprovementScore))]
        private static void GetImprovementScore_Citadel(
            GameState gameState,
            ImprovementData improvementData,
            TileData tileData,
            PlayerState player,
            ref float __result)
        {
            if (gameState == null || tileData == null || player == null || improvementData == null)
                return;

            if (improvementData.type != EnumCache<ImprovementData.Type>.GetType("citadel"))
                return;

            try
            {
                TileData rulingCity = gameState.Map.GetTile(tileData.rulingCityCoordinates);
                if (rulingCity?.improvement == null)
                    return;

                float score;

                // 1) Isolated land in water/ocean always prioritize
                if (MapAnalysis.IsIsolatedLandInWater(gameState, tileData))
                {
                    int expansionRadius = Math.Max(1, (int)rulingCity.improvement.borderSize);
                    int unclaimedCount = CountUnclaimedInRadius(gameState, tileData.coordinates, expansionRadius);

                    score = 1000f + unclaimedCount * 80f;
                }
                else
                {
                    // 2) Normal path pick the best corner from cache
                    GetCitadelCache(gameState, player);

                    if (cityCitadelCornerCache.TryGetValue(rulingCity.coordinates, out TileData? targetedCorner)
                        && targetedCorner != null
                        && tileData.coordinates.X == targetedCorner.coordinates.X
                        && tileData.coordinates.Y == targetedCorner.coordinates.Y)
                    {
                        int expansionRadius = rulingCity.improvement.borderSize;
                        int unclaimedCount = CountUnclaimedInRadius(gameState, tileData.coordinates, expansionRadius);
                        score = unclaimedCount * 80f;
                        if (rulingCity.improvement.borderSize == 2)
                            score *= 0.5f;
                    }
                    else
                    {
                        score = 0.1f; // non-corner, non-island
                    }
                }

                score *= AI.getPriceFactor(improvementData.cost, player);
                __result = score;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-AI] GetImprovementScore: {ex}");
            }
        }

        public static int CountUnclaimedInRadius(
            GameState gameState,
            WorldCoordinates center,
            int radius)
        {
            TileData[] nearby = gameState.Map.GetAreaSorted(center, radius, true, true);
            if (nearby == null)
                return 0;

            int unclaimed = 0;
            for (int i = 0; i < nearby.Length; i++)
            {
                if (nearby[i] != null && nearby[i].owner == 0)
                    unclaimed++;
            }
            return unclaimed;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(AI), nameof(AI.CheckForTechNeeds))]
        private static bool CheckForTechNeeds_WaterBias(
            GameState gameState,
            PlayerState player,
            // match vanilla signature — drop/adjust if your dump differs
            Il2CppSystem.Collections.Generic.List<TileData> playerEmpire,
            Il2CppSystem.Collections.Generic.Dictionary<TechData.Type, int> neededTech)
        {
            try
            {
                if (gameState?.Settings == null || player == null || neededTech == null)
                    return true; // vanilla

                if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                    return true;

                neededTech.Clear();

                int fieldForestCount = 0;
                int disconnectedCities = 0;
                var random = new System.Random();

                for (int i = 0; i < gameState.Map.Tiles.Length; i++)
                {
                    TileData tile = gameState.Map.Tiles[i];
                    if (tile == null || !tile.GetExplored(player.Id))
                        continue;

                    if (tile.owner == player.Id)
                    {
                        if (tile.HasImprovement(ImprovementData.Type.City) && !tile.IsConnected)
                            disconnectedCities++;

                        if (tile.terrain == TerrainData.Type.Field
                            || tile.terrain == TerrainData.Type.Forest)
                            fieldForestCount++;
                    }

                    // Terrain the player cannot access yet → tech need
                    if (!tile.CanBeAccessedByPlayer(gameState, player))
                    {
                        TechData unlockTech = gameState.GameLogicData.GetTechThatUnlocks(tile.terrain);
                        if (unlockTech != null)
                        {
                            // Water: much lower pressure (still occasional nudge)
                            int weight;
                            if (tile.IsWater)
                                weight = random.Next(0, 25) == 0 ? 1 : 0;
                            else
                                weight = 1;

                            if (weight > 0)
                                AI.AddTechNeed(neededTech, unlockTech.type, weight);
                        }
                    }

                    // Visible resource → freelance improvement tech
                    if (tile.resource != null
                        && gameState.GameLogicData.IsResourceVisibleToPlayer(
                            tile.resource.type, player, gameState))
                    {
                        var improvements = gameState.GameLogicData.GetImprovementForResource(
                            tile.resource.type);
                        if (improvements == null)
                            continue;

                        for (int j = 0; j < improvements.Count; j++)
                        {
                            ImprovementData imp = improvements[j];
                            if (imp == null)
                                continue;
                            if (!imp.HasAbility(ImprovementAbility.Type.Freelance))
                                continue;
                            if (gameState.GameLogicData.IsUnlocked(imp.type, player))
                                continue;

                            TribeData tribeData = gameState.GameLogicData.GetTribeData(player.tribe);
                            TechData tech = gameState.GameLogicData.GetTechThatUnlocks(imp, tribeData);
                            if (tech != null)
                                AI.AddTechNeed(neededTech, tech.type, 5);
                        }
                    }
                }

                // Roads when you have land tiles and disconnected cities
                if (fieldForestCount > 0)
                {
                    int roadsNeed = fieldForestCount * (1 + disconnectedCities);
                    AI.AddTechNeed(neededTech, TechData.Type.Roads, roadsNeed);
                }

                return false; // skip vanilla
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-AI] CheckForTechNeeds: {ex}");
                return true;
            }
        }

        // =========================================================================
        // Destroy / rebuild
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(AI), nameof(AI.GetTileCommands))]
        private static void GetTileCommands_DestroyCmd(
            GameState gameState,
            PlayerState player,
            CommandType specificCommand,
            ref CommandBase __result)
        {
            try
            {
                if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                    return;

                if (__result == null) return;
                if (player.currency < 30 || player.currency < (ResourceDataUtils.CalculateIncomeFor(gameState, player.Id) * 1.25 + 5) || gameState.CurrentTurn < 25) return;
                if (gameState.CurrentTurn % 2 != 0) return;

                var empireTiles = player.aiState?.PlayerMapData?.empireTiles;
                if (empireTiles == null) return;

                if (gameState.CurrentTurn != lastProcessedTurn || player.Id != lastProcessedPlayer)
                {
                    lastProcessedTurn = (int)gameState.CurrentTurn;
                    lastProcessedPlayer = player.Id;
                    processedTilesThisTurn.Clear();
                }

                GetCitadelCache(gameState, player);

                float bestScoreDifference = 0f;
                DestroyCommand? bestDestroyCommand = null;
                ImprovementData? bestOldType = null;
                ImprovementData? bestNewType = null;

                var citadelType = EnumCache<ImprovementData.Type>.GetType("citadel");

                foreach (TileData tileData in empireTiles)
                {
                    if (tileData == null || tileData.improvement == null) continue;
                    if (tileData.improvement.type == ImprovementData.Type.City
                        || tileData.improvement.type == ImprovementData.Type.LightHouse
                        || tileData.improvement.type == citadelType)
                        continue;
                    if (processedTilesThisTurn.Contains(tileData.coordinates)) continue;
                    if (!gameState.GameLogicData.TryGetData(tileData.improvement.type, out ImprovementData previousData))
                        continue;

                    float oldScore = MathF.Round(ForceGetImprovementScore(gameState, previousData, tileData, player));

                    if ((oldScore > 0f && previousData.rewards.GetPopulation() > 0)
                        || previousData.growthRewards.GetPopulation() > 0)
                    {
                        TileData city = gameState.Map.GetTile(tileData.rulingCityCoordinates);
                        if (city != null && city.CanCityBeUpgraded(gameState))
                        {
                            int needed = city.PopulationNeededToUpgradeCity()
                                - previousData.CalculateImprovementPopulationAtLevel((int)tileData.improvement.level);
                            if (needed > 0) oldScore += 200f / needed;
                        }
                    }

                    oldScore = MathF.Round(oldScore * AI.getPriceFactor(previousData.cost, player));

                    foreach (CommandBase commandBase in ForceGetBuildableImprovements(gameState, player, tileData, true))
                    {
                        BuildCommand buildCommand = commandBase.Cast<BuildCommand>();
                        if (!gameState.GameLogicData.TryGetData(buildCommand.Type, out ImprovementData currentData))
                            continue;

                        float newScore = ForceGetImprovementScore(gameState, currentData, tileData, player);

                        if (currentData.type == citadelType)
                        {
                            TileData capital = gameState.Map.GetTile(tileData.rulingCityCoordinates);
                            if (capital?.improvement == null)
                                continue;

                            int citadelCount = Main.CountCityCitadel(gameState, tileData);
                            if (Main.CityHasMaxCitadel(gameState, tileData, player, citadelCount))
                                continue;

                            bool isolatedIsland = MapAnalysis.IsIsolatedLandInWater(gameState, tileData);

                            bool isBestCorner =
                                cityCitadelCornerCache.TryGetValue(tileData.rulingCityCoordinates, out TileData? corner)
                                && corner != null
                                && tileData.coordinates.X == corner.coordinates.X
                                && tileData.coordinates.Y == corner.coordinates.Y;

                            // Only citadel on best corner OR isolated land in water
                            if (!isolatedIsland && !isBestCorner)
                                continue;

                            int border = Math.Max(1, (int)capital.improvement.borderSize);
                            int unclaimed = 0;
                            TileData[] nearby = MapDataExtensions.GetAreaSorted(
                                gameState.Map, tileData.coordinates, border, true, true);
                            if (nearby != null)
                            {
                                for (int i = 0; i < nearby.Length; i++)
                                {
                                    if (nearby[i] != null && nearby[i].owner == 0)
                                        unclaimed++;
                                }
                            }

                            if (isolatedIsland)
                            {
                                newScore = 1000f + unclaimed * 75f;
                            }
                            else
                            {
                                newScore = (float)(unclaimed * 75 / Math.Pow(border, 1.5));
                            }
                        }

                        if ((newScore > 0f && currentData.rewards.GetPopulation() > 0)
                            || currentData.growthRewards.GetPopulation() > 0)
                        {
                            TileData city = gameState.Map.GetTile(tileData.rulingCityCoordinates);
                            if (city != null && city.CanCityBeUpgraded(gameState))
                            {
                                int needed = city.PopulationNeededToUpgradeCity()
                                    - previousData.CalculateImprovementPopulationAtLevel((int)tileData.improvement.level);
                                if (needed > 0) newScore += 200f / needed;
                            }
                        }

                        newScore = MathF.Round(newScore * AI.getPriceFactor(currentData.cost, player));

                        if (newScore > oldScore && newScore > 150
                            && !previousData.type.IsMonument()
                            && !currentData.type.IsMonument())
                        {
                            float difference = newScore - oldScore;
                            if (difference > bestScoreDifference)
                            {
                                var destroyCommand = new DestroyCommand(player.Id, tileData.coordinates);
                                if (destroyCommand.IsValid(gameState))
                                {
                                    bestScoreDifference = difference;
                                    bestDestroyCommand = destroyCommand;
                                    bestOldType = previousData;
                                    bestNewType = currentData;
                                }
                            }
                        }
                    }
                }

                if (bestDestroyCommand == null) return;

                TileData commandTile = gameState.Map.GetTile(bestDestroyCommand.Coordinates);

                if (bestOldType != null && bestNewType != null && bestOldType.type == bestNewType.type)
                    return;

                if (bestNewType != null && bestNewType.HasAbility(ImprovementAbility.Type.Consumed))
                    return;

                if (bestNewType != null && bestNewType.type == ImprovementData.Type.Market && commandTile.improvement.level > 2)
                    return;

                if (commandTile.unit != null && commandTile.unit.owner != commandTile.owner)
                    return;

                processedTilesThisTurn.Add(bestDestroyCommand.Coordinates);
                __result = bestDestroyCommand;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-AI] GetTileCommands_DestroyCmd: {ex}");
            }
        }

        // =========================================================================
        // C. Military — prevent stiff suicide
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(PathFinder), nameof(PathFinder.GetMoveOptions))]
        private static void GetMoveOptions_PreventSuicide(
            GameState gameState,
            WorldCoordinates start,
            int maxCost,
            UnitState unit,
            ref Il2CppSystem.Collections.Generic.List<WorldCoordinates> __result)
        {
            if (skipMoveOptionsPatch)
                return;

            try
            {
                if (gameState?.Settings == null || unit == null || __result == null)
                    return;

                var mode = gameState.Settings.RulesGameMode;
                if (mode != EnumCache<GameMode>.GetType("conquest")
                    && mode != EnumCache<GameMode>.GetType("reign"))
                    return;

                if (!gameState.TryGetPlayer(unit.owner, out PlayerState player) || !player.AutoPlay)
                    return;

                if (!unit.UnitData.HasAbility(UnitAbility.Type.Stiff)
                    || unit.type == UnitData.Type.Juggernaut
                    || unit.HasAbility(UnitAbility.Type.Infiltrate))
                    return;

                HashSet<WorldCoordinates> danger = GetDangerousTilesCached(gameState, player);

                for (int i = __result.Count - 1; i >= 0; i--)
                {
                    if (danger.Contains(__result[i]))
                        __result.RemoveAt(i);
                }

                if (__result.Count == 0)
                    __result.Add(start);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-AI] GetMoveOptions_PreventSuicide: {ex}");
            }
        }

        // =========================================================================
        // Helpers — cache
        // =========================================================================
        private static void GetCitadelCache(GameState gameState, PlayerState player)
        {
            if (citadelCacheTurn == gameState.CurrentTurn) return;

            cityCitadelCornerCache.Clear();
            foreach (TileData city in player.GetCityTiles(gameState))
            {
                if (city == null) continue;
                CityAnalysisResult? result = ForceScanCornerForCitadel(
                    gameState.Map, gameState, city, 5, player, Faction.Both, false);
                if (result?.TargetTile != null)
                    cityCitadelCornerCache[city.coordinates] = result.TargetTile;
            }
            citadelCacheTurn = (int)gameState.CurrentTurn;
        }

         private static HashSet<WorldCoordinates> GetDangerousTilesCached(
            GameState gameState, PlayerState player)
        {
            if (dangerousCacheTurn != gameState.CurrentTurn)
            {
                dangerousTilesCache.Clear();
                dangerousCacheTurn = (int)gameState.CurrentTurn;
            }

            if (dangerousTilesCache.TryGetValue(player.Id, out HashSet<WorldCoordinates>? cached))
                return cached;

            HashSet<WorldCoordinates> set = MapAnalysis.BuildDangerSetFromOptions(gameState, player);
            dangerousTilesCache[player.Id] = set;
            return set;
        }

        // =========================================================================
        // Helpers — get
        // =========================================================================
        public static CityAnalysisResult? ForceScanCornerForCitadel(
            MapData map,
            GameState gameState,
            TileData cityTile,
            int searchRadius,
            PlayerState currentOwner,
            Faction findType = Faction.Both,
            bool findMost = false)
        {
            return MapAnalysis.ScanCityForCorners(
                map, gameState, cityTile, searchRadius, currentOwner,
                findType, findMost, requireEmptyTile: false);
        }

        private static Il2CppSystem.Collections.Generic.List<CommandBase> ForceGetBuildableImprovements(
            GameState gameState, PlayerState player, TileData tile, bool includeUnavailable = false)
        {
            var list = new Il2CppSystem.Collections.Generic.List<CommandBase>();
            if (player.Id != gameState.CurrentPlayer) return list;

            foreach (ImprovementData improvementData in gameState.GameLogicData.GetUnlockedImprovements(player))
            {
                if (improvementData.HasAbility(ImprovementAbility.Type.Manual)) continue;
                if (player.currency < improvementData.cost) continue;

                if (gameState.GameLogicData.MeetsRequirement(tile, improvementData, player, gameState)
                    && gameState.GameLogicData.MeetsAdjacencyRequirement(
                        gameState.Map, tile, improvementData.adjacencyRequirements))
                {
                    var command = new BuildCommand(player.Id, improvementData.type, tile.coordinates);
                    if (includeUnavailable || command.IsValid(gameState))
                        list.Add(command);
                }
            }

            return list;
        }

        private static float ForceGetImprovementScore(
            GameState gameState, ImprovementData improvementData, TileData tileData, PlayerState player)
        {
            float score = 0f;
            int population = improvementData.rewards.GetPopulation();
            score += population * 25f;
            score += improvementData.rewards.GetCurrency() * 2f;
            score += improvementData.work * 20f;

            if (improvementData.HasAbility(ImprovementAbility.Type.Patina))
            {
                population = improvementData.growthRewards.GetPopulation();
                score += population * 100f;
            }

            UnitData createdUnit = improvementData.creates.GetUnit();
            if (createdUnit != null)
            {
                AI.UnitStats desired = AI.GetDesiredUnitStats(gameState, player, tileData);
                score += AI.GetBuildUnitScore(gameState, player, createdUnit, desired, tileData);
            }

            if (improvementData.adjacencyImprovements != null && improvementData.adjacencyImprovements.Count > 0)
            {
                int adjacency = ActionUtils.GetAdjacencyBonusAt(gameState, tileData, improvementData);
                if (improvementData.adjacencyImprovements.Contains(ImprovementData.Type.PolarisClimate))
                    adjacency += player.aiState.frozenTileCount / 20;
                score += improvementData.growthRewards.GetPopulation() * 20 * adjacency;
                score += improvementData.work * 20 * adjacency;
            }

            if (tileData.rulingCityCoordinates != WorldCoordinates.NULL_COORDINATES)
            {
                TileData city = gameState.Map.GetTile(tileData.rulingCityCoordinates);
                if (city?.improvement != null
                    && city.improvement.type == ImprovementData.Type.City
                    && (int)city.improvement.xp + population > (int)city.improvement.level
                        - improvementData.CalculateImprovementPopulationAtLevel((int)tileData.improvement.level))
                {
                    score += 100f;
                }

                if (improvementData.IsRouteOpener() && city != null)
                {
                    bool hasRoute = false;
                    foreach (TileData nearbyTile in gameState.Map.GetArea(city.coordinates, 1, true, false))
                    {
                        if (nearbyTile.improvement != null
                            && gameState.GameLogicData.TryGetData(nearbyTile.improvement.type, out ImprovementData data)
                            && data.IsRouteOpener())
                        {
                            hasRoute = true;
                            break;
                        }
                    }

                    if (!city.IsConnected && !hasRoute)
                    {
                        score += 30f;
                        if (city.coordinates == player.startTile)
                            score += 50f;
                    }
                }
            }

            if (gameState.Settings.RulesGameMode == (GameMode)1)
                score += improvementData.rewards.GetScore() / 10f;

            score += AI.GetImprovementAbilityScore(gameState, player, improvementData, tileData);

            if (improvementData.type != ImprovementData.Type.Road
                && tileData.resource != null
                && !gameState.GameLogicData.IsResourceRequiredByImprovement(tileData.resource.type, improvementData))
            {
                score *= 0.5f;
            }

            return score;
        }
    }
}