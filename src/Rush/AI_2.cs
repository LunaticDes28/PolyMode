using HarmonyLib;
using Il2CppSystem.Threading.Tasks;
using Polytopia.Data;
using PolytopiaBackendBase.Game;
using PolyMode;

namespace Rush
{
    public static class AI_2
    {
        // =========================================================================
        // Caches
        // =========================================================================
        public static int lastProcessedTurn = -1;
        public static byte lastProcessedPlayer = 255;
        public static readonly HashSet<WorldCoordinates> processedTilesThisTurn = new HashSet<WorldCoordinates>();

        public static readonly Dictionary<byte, HashSet<WorldCoordinates>> dangerousTilesCache =
            new Dictionary<byte, HashSet<WorldCoordinates>>();
        public static int dangerousCacheTurn = -1;
        public static bool skipMoveOptionsPatch = false;

        // =========================================================================
        // A. Diplomacy
        // =========================================================================
        [HarmonyPrefix]
        [HarmonyPatch(typeof(AI), nameof(AI.GetGameProgress))]
        private static bool GetGameProgress_Rush(
            ref float __result, GameState gameState, PlayerState winningPlayer)
        {
            if (gameState?.Settings == null) return true;
            try
            {
                if (winningPlayer == null)
                {
                    __result = 0f;
                    return false;
                }

                if (gameState.Settings.RulesGameMode == EnumCache<GameMode>.GetType("rusha"))
                {
                    float incomeProgress = ResourceDataUtils.CalculateIncomeFor(gameState, winningPlayer.Id) / (winningPlayer.cities * 15);
                    __result = Math.Min(1f, Math.Max(0f, incomeProgress));
                    return false;
                }
                if (gameState.Settings.RulesGameMode == EnumCache<GameMode>.GetType("rushb"))
                {
                    Il2CppSystem.Collections.Generic.List<TileData> list = new Il2CppSystem.Collections.Generic.List<TileData>();
                    gameState.Map.GetPlayerCityTiles(winningPlayer.Id, list);
                    short maxUnit = 0;
                    foreach (TileData tileData in list)
                    {
                        maxUnit += (short)((short)tileData.improvement.level + 1);
                    }

                    float militaryProgress = winningPlayer.CountUnits(gameState) / maxUnit;
                    __result = Math.Min(1f, Math.Max(0f, militaryProgress));
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Rush-AI] GetGameProgress: {ex.Message}");
                __result = 0f;
                return false;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(AI), nameof(AI.RateBattle))]
        private static void RateBattle_Cities(
            GameState gameState, UnitState attackingUnit, TileData defendingTile, ref float __result)
        {
            if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("rushb")) return;

            if (defendingTile.owner == 0) return;

            PlayerState winningPlayer = gameState.GetPlayersSortedByRank()[0];
            float advantageFactor = (float)Math.Pow(gameState.CurrentTurn / Math.MaxMagnitude(1, gameState.Settings.rules.TurnLimit - 1), 5);
            if (advantageFactor > 0f)
            {
                __result += advantageFactor;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(OpinionManager), nameof(OpinionManager.UpdateOpinion))]
        private static void UpdateOpinion_Cities(OpinionManager __instance, GameState gameState, PlayerState player, PlayerState opponent)
        {
            if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("rushb")) return;

            if (player == opponent || player.Id == 255 || opponent.Id == 255 || !opponent.IsAlive(gameState)) return;

            if (player.GetRelation(opponent.Id).FirstMeet < 0
                && opponent.GetRelation(player.Id).LastAttackTurn < 0)
                return;

            float advantageFactor = (float)Math.Pow(gameState.CurrentTurn / Math.MaxMagnitude(1, gameState.Settings.rules.TurnLimit - 1), 3);

            float hate = advantageFactor;
            var opinionState = new OpinionState();

            if (advantageFactor > 0f)
            {
                opinionState.AddOpinion(hate * 1f, OpinionManager.Type.Winning);
            }

            if (!__instance.Opinions.ContainsKey(opponent.Id))
                __instance.Opinions[opponent.Id] = new OpinionState();

            __instance.Opinions[opponent.Id].AddOpinion(
                opinionState.GetOpinion(OpinionManager.Type.Winning) * -1f,
                OpinionManager.Type.Winning);
        }

        // =========================================================================
        // B. Development
        // =========================================================================

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

        // =========================================================================
        // C. Destroy
        // =========================================================================
        /*[HarmonyPostfix]
        [HarmonyPatch(typeof(AI), nameof(AI.GetTileCommands))]
        private static void GetTileCommands_DestroyCmd(
            GameState gameState,
            PlayerState player,
            CommandType specificCommand,
            ref CommandBase __result)
        {
            try
            {
                if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("rusha")
                    && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("rushb"))
                {
                    return;
                }

                if (specificCommand != CommandType.None && specificCommand != CommandType.Destroy)
                {
                    return;
                }

                if (__result == null) return;
                if (player.currency < 30 || player.currency < (ResourceDataUtils.CalculateIncomeFor(gameState, player.Id) * 1.25 + 5) || gameState.CurrentTurn < 25) return;
                if (!player.AutoPlay) return;
                if (gameState.CurrentTurn % 3 != 0) return;

                var empireTiles = player.aiState?.PlayerMapData?.empireTiles;
                if (empireTiles == null) return;

                if (gameState.CurrentTurn != lastProcessedTurn || player.Id != lastProcessedPlayer)
                {
                    lastProcessedTurn = (int)gameState.CurrentTurn;
                    lastProcessedPlayer = player.Id;
                    processedTilesThisTurn.Clear();
                }

                float bestScoreDifference = 0f;
                DestroyCommand? bestDestroyCommand = null;
                ImprovementData? bestOldType = null;
                ImprovementData? bestNewType = null;

                foreach (TileData tileData in empireTiles)
                {
                    if (tileData == null || tileData.improvement == null) continue;
                    if (tileData.improvement.type == ImprovementData.Type.City || tileData.improvement.type == ImprovementData.Type.LightHouse) continue;
                    if (processedTilesThisTurn.Contains(tileData.coordinates)) continue;
                    if (!gameState.GameLogicData.TryGetData(tileData.improvement.type, out ImprovementData previousData)) continue;

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
                        if (!gameState.GameLogicData.TryGetData(buildCommand.Type, out ImprovementData currentData)) continue;

                        float newScore = ForceGetImprovementScore(gameState, currentData, tileData, player);

                        TileData city = gameState.Map.GetTile(tileData.rulingCityCoordinates);
                        if ((newScore > 0f && currentData.rewards.GetPopulation() > 0) || currentData.growthRewards.GetPopulation() > 0)
                        {
                            if (city != null && city.CanCityBeUpgraded(gameState))
                            {
                                int needed = city.PopulationNeededToUpgradeCity() - previousData.CalculateImprovementPopulationAtLevel((int)tileData.improvement.level);
                                if (needed > 0) newScore += 200f / needed;
                            }
                        }

                        newScore = MathF.Round(newScore * AI.getPriceFactor(currentData.cost, player));

                        if (newScore > oldScore && newScore > 150
                            && city?.improvement.level >= 4
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

                if (bestOldType != null && bestNewType != null && bestOldType.type == bestNewType.type) return;

                if (bestNewType != null && bestNewType.HasAbility(ImprovementAbility.Type.Consumed)) return;

                if (bestNewType != null && bestNewType.type == ImprovementData.Type.Market && commandTile.improvement.level > 2) return;

                if (commandTile.unit != null && commandTile.unit.owner != commandTile.owner) return;

                processedTilesThisTurn.Add(bestDestroyCommand.Coordinates);
                __result = bestDestroyCommand;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Rush-AI] GetTileCommands_DestroyCmd: {ex}");
            }
        }*/

        // =========================================================================
        // D. Military
        // =========================================================================
        /*[HarmonyPostfix]
        [HarmonyPatch(typeof(PathFinder), nameof(PathFinder.GetMoveOptions))]
        private static void GetMoveOptions_Combined(
            GameState gameState,
            WorldCoordinates start,
            int maxCost,
            UnitState unit,
            ref Il2CppSystem.Collections.Generic.List<WorldCoordinates> __result)
        {
            if (skipMoveOptionsPatch) return;

            try
            {
                if (gameState?.Settings == null || unit == null || __result == null) return;

                var mode = gameState.Settings.RulesGameMode;
                if (mode != EnumCache<GameMode>.GetType("rusha") && mode != EnumCache<GameMode>.GetType("rushb"))
                {
                    return;
                }

                if (!gameState.TryGetPlayer(unit.owner, out PlayerState player) || !player.AutoPlay)
                {
                    return;
                }

                // -----------------------------------------------------------------
                // 0) Weak Complex
                // -----------------------------------------------------------------
                TileData startTile = gameState.Map.GetTile(start);
                if (startTile != null
                    && startTile.improvement != null
                    && startTile.improvement.type == ImprovementData.Type.City
                    && startTile.owner == unit.owner
                    && IsWeakCityComplex(gameState, unit))
                {
                    HashSet<WorldCoordinates> danger = GetDangerousTilesCached(gameState, player);
                    if (danger.Contains(start))
                    {
                        var leaveSafe = new Il2CppSystem.Collections.Generic.List<WorldCoordinates>();
                        var leaveAny = new Il2CppSystem.Collections.Generic.List<WorldCoordinates>();

                        for (int i = 0; i < __result.Count; i++)
                        {
                            WorldCoordinates c = __result[i];
                            if (c == start) continue;

                            leaveAny.Add(c);
                            if (!danger.Contains(c))
                            {
                                leaveSafe.Add(c);
                            }
                        }

                        if (leaveSafe.Count > 0)
                        {
                            __result = leaveSafe;
                        }
                        else if (leaveAny.Count > 0)
                        {
                            __result = leaveAny;
                        }

                        return; // don't also run Stiff/Escape
                    }
                }

                // -----------------------------------------------------------------
                // 1) Stiff
                // -----------------------------------------------------------------
                if (unit.UnitData.HasAbility(UnitAbility.Type.Stiff)
                    && unit.type != UnitData.Type.Juggernaut
                    && !unit.HasAbility(UnitAbility.Type.Infiltrate))
                {
                    //Loader.modLogger?.LogInfo($"[Conquest-AI] Stiff detected");
                    HashSet<WorldCoordinates> danger = GetDangerousTilesCached(gameState, player);

                    for (int i = __result.Count - 1; i >= 0; i--)
                    {
                        if (danger.Contains(__result[i]))
                        {
                            __result.RemoveAt(i);
                        }
                    }
                }

                // -----------------------------------------------------------------
                // 2) Rider
                // -----------------------------------------------------------------
                if (unit.UnitData.HasAbility(UnitAbility.Type.Escape) && unit.attacked)
                //if (unit.type == UnitData.Type.Rider)
                {
                    //Loader.modLogger?.LogInfo($"[Rush-AI] Escape has attacked");
                    List<WorldCoordinates> enemyPositions = MapAnalysis.CollectEnemyPositions(gameState, start, 7, player.Id);
                    if (enemyPositions.Count == 0 || __result.Count == 0) return;
                    //Loader.modLogger?.LogInfo($"[Rush-AI] Enemy count for escape not null");

                    WorldCoordinates bestTile = WorldCoordinates.NULL_COORDINATES;
                    int bestMinDist = int.MinValue;
                    var scored = new List<(WorldCoordinates tile, int minDist)>();

                    for (int i = 0; i < __result.Count; i++)
                    {
                        TileData tileData = gameState.Map.GetTile(__result[i]);
                        int minDist = MapAnalysis.MinChebyshevDistanceToEnemies(__result[i], enemyPositions);
                        if (minDist > bestMinDist && tileData.unit == null)
                        {
                            bestTile = __result[i];
                            bestMinDist = minDist;
                        }

                        if (tileData?.improvement != null
                            && tileData.improvement.type == ImprovementData.Type.City
                            && tileData.owner != unit.owner)
                        {
                            scored.Add((__result[i], 0));
                        }
                    }

                    //Loader.modLogger?.LogInfo($"[Rush-AI] Best escape location calculated");
                    
                    if (bestTile != WorldCoordinates.NULL_COORDINATES && !scored.Contains((bestTile, bestMinDist)))
                    {
                        scored.Add((bestTile, bestMinDist));
                    }

                    __result = new Il2CppSystem.Collections.Generic.List<WorldCoordinates>();
                    //Loader.modLogger?.LogInfo($"[Conquest-AI] New list created for escape");
                    for (int i = 0; i < scored.Count; i++)
                    {
                        if (scored[i].tile != WorldCoordinates.NULL_COORDINATES)
                        {
                            __result.Add(scored[i].tile);
                            //Loader.modLogger?.LogInfo($"[Conquest-AI] Escape tile is {scored[i].tile} and count is {scored.Count}");
                        }
                    }
                }

                return;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Rush-AI] GetMoveOptions_Combined: {ex}");
            }
        }*/

        // =========================================================================
        // Helpers — cache
        // =========================================================================
        public static HashSet<WorldCoordinates> GetDangerousTilesCached(
            GameState gameState, PlayerState player)
        {
            if (dangerousCacheTurn != gameState.CurrentTurn)
            {
                dangerousTilesCache.Clear();
                dangerousCacheTurn = (int)gameState.CurrentTurn;
            }

            if (dangerousTilesCache.TryGetValue(player.Id, out HashSet<WorldCoordinates>? cached))
            {
                return cached;
            }

            HashSet<WorldCoordinates> set = MapAnalysis.BuildDangerSetFromOptions(gameState, player);
            dangerousTilesCache[player.Id] = set;
            return set;
        }

        // =========================================================================
        // Helpers — get
        // =========================================================================
        public static Il2CppSystem.Collections.Generic.List<CommandBase> ForceGetBuildableImprovements(
            GameState gameState, PlayerState player, TileData tile, bool includeUnavailable = false)
        {
            var list = new Il2CppSystem.Collections.Generic.List<CommandBase>();
            if (player.Id != gameState.CurrentPlayer) return list;

            foreach (ImprovementData improvementData in gameState.GameLogicData.GetUnlockedImprovements(player))
            {
                if (improvementData.HasAbility(ImprovementAbility.Type.Manual)) continue;
                if (player.currency < improvementData.cost) continue;

                if (gameState.GameLogicData.MeetsRequirement(tile, improvementData, player, gameState)
                    && gameState.GameLogicData.MeetsAdjacencyRequirement(gameState.Map, tile, improvementData.adjacencyRequirements))
                {
                    var command = new BuildCommand(player.Id, improvementData.type, tile.coordinates);
                    if (includeUnavailable || command.IsValid(gameState))
                    {
                        list.Add(command);
                    }
                }
            }

            return list;
        }

        public static float ForceGetImprovementScore(
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

        private static bool IsWeakCityComplex(GameState gameState, UnitState unit)
        {
            try
            {
                if (unit?.UnitData == null) return false;
                int def = unit.GetDefence(gameState);
                int hp = unit.health;
                int maxHp = unit.UnitData.health;
                if (unit.type == UnitData.Type.Defender) return false;
                if (def <= 3) return true;
                if (maxHp > 0 && hp <= maxHp / 2) return true;
            }
            catch { }
            return false;
        }
    }
}