using HarmonyLib;
using Il2CppInterop.Runtime.Runtime;
using Polytopia.Data;
using PolytopiaBackendBase;
using PolytopiaBackendBase.Game;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PolyMode
{
    public static class AI_2
    {
        // Toggle this to true only when you need debug logs
        private const bool DEBUG_AI = true;

        // =========================================================================
        // Caches (cleared / updated per turn)
        // =========================================================================
        private static int lastProcessedTurn = -1;
        private static byte lastProcessedPlayer = 255;
        private static readonly HashSet<WorldCoordinates> processedTilesThisTurn = new HashSet<WorldCoordinates>();

        // City → best citadel corner (lives across turns until borders change)
        private static readonly Dictionary<WorldCoordinates, TileData> cityCitadelCornerCache = new Dictionary<WorldCoordinates, TileData>();
        private static int citadelCacheTurn = -1;

        // Dangerous tiles cache per player+turn
        private static readonly Dictionary<byte, HashSet<WorldCoordinates>> dangerousTilesCache = new Dictionary<byte, HashSet<WorldCoordinates>>();
        private static int dangerousCacheTurn = -1;

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
                return false;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(OpinionManager), nameof(OpinionManager.UpdateOpinion))]
        private static void UpdateOpinion_Cities(OpinionManager __instance, GameState gameState, PlayerState player, PlayerState opponent)
        {
            if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                return;

            if (player == opponent || player.Id == 255 || opponent.Id == 255 || !opponent.IsAlive(gameState))
                return;

            if (player.GetRelation(opponent.Id).FirstMeet < 0 && opponent.GetRelation(player.Id).LastAttackTurn < 0)
                return;

            float cityAdvantage = opponent.GetCityAdvantage(gameState);
            if (cityAdvantage <= 0f) return;

            float hate = cityAdvantage * 1f;
            OpinionState opinionState = new OpinionState();
            opinionState.AddOpinion(hate * 2.3f, EnumCache<OpinionManager.Type>.GetType("obstinate"));
            opinionState.AddOpinion(hate * 1.2f, OpinionManager.Type.Winning);

            if (!__instance.Opinions.ContainsKey(opponent.Id))
                __instance.Opinions[opponent.Id] = new OpinionState();

            __instance.Opinions[opponent.Id].AddOpinion(opinionState.GetOpinion(OpinionManager.Type.Winning) * -1f, OpinionManager.Type.Winning);
            __instance.Opinions[opponent.Id].AddOpinion(
                opinionState.GetOpinion(EnumCache<OpinionManager.Type>.GetType("obstinate")) * -1f,
                EnumCache<OpinionManager.Type>.GetType("obstinate"));
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(AI), nameof(AI.RateBattle))]
        private static void RateBattle_Cities(GameState gameState, UnitState attackingUnit, TileData defendingTile, ref float __result)
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
            foreach (PlayerState p in state.PlayerStates)
            {
                if (p.Id == 255) continue;
                total += p.IsAlive(state) ? p.CountCities(state) : 0;
                count++;
            }
            return count > 0 ? (float)total / count : 0f;
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
                return;

            if (!gameState.TryGetPlayer(tile.owner, out PlayerState playerState)
                || !gameState.GameLogicData.TryGetData(playerState.tribe, out TribeData tribeData))
                return;

            System.Random random = new System.Random();

            if (tile.improvement.level == 2)
            {
                CityAnalysisResult? centerResult = MapAnalysis.ScanCity(gameState.Map, gameState, tile, 3, true, playerState);

                if (centerResult != null && centerResult.EnemyCityCount >= 2
                    && centerResult.EnemyCityCount - centerResult.OwnedCityCount >= 2)
                {
                    if (DEBUG_AI)
                    {
                        Loader.modLogger?.LogInfo($"[Conquest-Evacuation] Isolated city at ({tile.coordinates.X},{tile.coordinates.Y}) → Evacuation");
                    }
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
                int num = random.Next(0, 3);
                if (num == 0) __result = CityReward.CityWall;
                else if (num == 1) __result = CityReward.Resources;
                else __result = EnumCache<CityReward>.GetType("valhalla");
            }
            else if (tile.improvement.level == 4)
            {
                CityAnalysisResult? farResult = MapAnalysis.ScanCity(gameState.Map, gameState, tile, 8, true, playerState);

                if (farResult != null && farResult.EnemyCityCount == 0 && playerState.cities >= 4)
                {
                    if (DEBUG_AI)
                    {
                        Loader.modLogger?.LogInfo($"[Conquest-Tax] Protected city at ({tile.coordinates.X},{tile.coordinates.Y}) → Tax Reform");
                    }
                    __result = EnumCache<CityReward>.GetType("taxreform");
                }
                else if (farResult != null && farResult.OwnedCityCount >= 5)
                {
                    __result = EnumCache<CityReward>.GetType("taxreform");
                }
                else
                {
                    __result = random.Next(0, 2) == 0 ? CityReward.BorderGrowth : CityReward.PopulationGrowth;
                }
            }
            else if (tile.improvement.level >= 5)
            {
                __result = CityReward.SuperUnit;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(AI), nameof(AI.GetImprovementScore))]
        private static void GetImprovementScore_Citadel(GameState gameState, ImprovementData improvementData, TileData tileData, PlayerState player, ref float __result)
        {
            if (gameState == null || tileData == null || player == null) return;

            // Only run expensive logic for the citadel itself
            if (improvementData.type != EnumCache<ImprovementData.Type>.GetType("citadel")
                && !gameState.GameLogicData.IsUnlocked(improvementData.type, player))
                return;

            try
            {
                float num = 0f;
                TileData rulingCity = gameState.Map.GetTile(tileData.rulingCityCoordinates);
                if (rulingCity == null || rulingCity.improvement == null) return;

                // Use cached corner if available
                EnsureCitadelCache(gameState, player);

                if (cityCitadelCornerCache.TryGetValue(rulingCity.coordinates, out TileData? targetedCornerTile)
                    && targetedCornerTile != null
                    && tileData.coordinates.X == targetedCornerTile.coordinates.X
                    && tileData.coordinates.Y == targetedCornerTile.coordinates.Y)
                {
                    int expansionRadius = rulingCity.improvement.borderSize;
                    TileData[] nearbyTiles = gameState.Map.GetAreaSorted(
                        new WorldCoordinates(tileData.coordinates.X, tileData.coordinates.Y),
                        expansionRadius, true, true);

                    int unclaimedCount = 0;
                    if (nearbyTiles != null)
                    {
                        foreach (var t in nearbyTiles)
                            if (t != null && t.owner == 0) unclaimedCount++;
                    }

                    float totalStrategicScore = unclaimedCount * 80f;

                    // Simple military bonus (avoid another full ScanCity here)
                    totalStrategicScore += 0f; // keep light; heavy scan already done in cache

                    num += totalStrategicScore;

                    if (rulingCity.improvement.borderSize == 2)
                        num *= 0.5f;
                }
                else
                {
                    num *= 0.1f;
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
        // Destroy / Rebuild logic (heavily throttled)
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(AI), nameof(AI.GetTileCommands))]
        private static void GetTileCommands_DestroyCmd(GameState gameState, PlayerState player, CommandType specificCommand, ref CommandBase __result)
        {
            try
            {
                if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                    return;

                if (__result == null) return;
                if (player.Currency < 30) return;                 // not worth evaluating
                if (gameState.CurrentTurn % 2 != 0) return;       // only every 2 turns

                var empireTiles = player.aiState?.PlayerMapData?.empireTiles;
                if (empireTiles == null) return;

                // Reset per-turn tracking
                if (gameState.CurrentTurn != lastProcessedTurn || player.Id != lastProcessedPlayer)
                {
                    lastProcessedTurn = (int)gameState.CurrentTurn;
                    lastProcessedPlayer = player.Id;
                    processedTilesThisTurn.Clear();
                }

                EnsureCitadelCache(gameState, player);

                float globalBestScoreDifference = 0f;
                DestroyCommand? globalBestDestroyCmd = null;
                string globalOldName = "", globalNewName = "";
                float globalOldScore = 0f, globalNewScore = 0f;

                foreach (TileData tileData in empireTiles)
                {
                    if (tileData == null || tileData.improvement == null) continue;
                    if (tileData.improvement.type == ImprovementData.Type.City
                        || tileData.improvement.type == ImprovementData.Type.LightHouse
                        || tileData.improvement.type == EnumCache<ImprovementData.Type>.GetType("citadel"))
                        continue;

                    if (processedTilesThisTurn.Contains(tileData.coordinates)) continue;

                    if (!gameState.GameLogicData.TryGetData(tileData.improvement.type, out ImprovementData previousData))
                        continue;

                    float oldScore = ForceGetImprovementScore(gameState, previousData, tileData, player);
                    oldScore = (float)Math.Round(oldScore);

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
                    oldScore *= AI.getPriceFactor(previousData.cost, player);
                    oldScore = (float)Math.Round(oldScore);

                    foreach (CommandBase commandBase in ForceGetBuildableImprovements(gameState, player, tileData, true))
                    {
                        BuildCommand buildCommand = commandBase.Cast<BuildCommand>();
                        if (!gameState.GameLogicData.TryGetData(buildCommand.Type, out ImprovementData currentData))
                            continue;

                        float newScore = ForceGetImprovementScore(gameState, currentData, tileData, player);

                        // Special citadel scoring from cache
                        if (currentData.type == EnumCache<ImprovementData.Type>.GetType("citadel"))
                        {
                            TileData capital = gameState.Map.GetTile(tileData.rulingCityCoordinates);
                            if (cityCitadelCornerCache.TryGetValue(tileData.rulingCityCoordinates, out TileData? corner)
                                && corner != null
                                && tileData.coordinates.X == corner.coordinates.X
                                && tileData.coordinates.Y == corner.coordinates.Y)
                            {
                                int unclaimed = 0;
                                TileData[] nearby = MapDataExtensions.GetAreaSorted(
                                    gameState.Map, tileData.coordinates, capital.improvement.borderSize, true, true);
                                if (nearby != null)
                                {
                                    for (int i = 0; i < nearby.Length; i++)
                                        if (nearby[i] != null && nearby[i].owner == 0) unclaimed++;
                                }
                                newScore = (float)(unclaimed * 75 / Math.Pow(capital.improvement.borderSize, 1.5));
                            }
                            else continue;
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

                        newScore *= AI.getPriceFactor(currentData.cost, player);
                        newScore = (float)Math.Round(newScore);

                        if (newScore > oldScore && newScore > 150
                            && !previousData.type.IsMonument() && !previousData.type.IsTemple()
                            && !currentData.type.IsMonument() && !currentData.type.IsTemple())
                        {
                            float diff = newScore - oldScore;
                            if (diff > globalBestScoreDifference)
                            {
                                DestroyCommand destroyCmd = new DestroyCommand(player.Id, tileData.coordinates);
                                if (destroyCmd.IsValid(gameState))
                                {
                                    globalBestScoreDifference = diff;
                                    globalBestDestroyCmd = destroyCmd;
                                    globalOldName = previousData.type.GetDisplayName();
                                    globalNewName = currentData.type.GetDisplayName();
                                    globalOldScore = oldScore;
                                    globalNewScore = newScore;
                                }
                            }
                        }
                    }
                }

                if (globalBestDestroyCmd != null)
                {
                    TileData cmdTile = gameState.Map.GetTile(globalBestDestroyCmd.Coordinates);
                    if (!(cmdTile.unit != null && cmdTile.unit.owner != cmdTile.owner))
                    {
                        processedTilesThisTurn.Add(globalBestDestroyCmd.Coordinates);
                        __result = globalBestDestroyCmd;

                        if (DEBUG_AI)
                        {
                            Loader.modLogger?.LogInfo(
                                $"[Conquest-AI] Destroy {globalOldName} ({globalOldScore}) → {globalNewName} ({globalNewScore}) " +
                                $"[+{globalBestScoreDifference}] at {globalBestDestroyCmd.Coordinates}");
                        }
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
                    return;

                if (!player.AutoPlay || tile == null) return;

                if (unit.HasAbility(UnitAbility.Type.Stiff))
                {
                    if (IsTileDangerous(gameState, player, tile.coordinates, 6))
                    {
                        __result = 0f;
                        return;
                    }
                }

                if (tile.IsWater)
                {
                    var empireTiles = player.aiState?.PlayerMapData?.empireTiles;
                    if (empireTiles != null && empireTiles.Contains(tile) && IsWaterUnderMyControl(gameState, tile, player))
                    {
                        if (unit.type == UnitData.Type.Rammership)
                            __result = 0f;
                        else if (unit.type == UnitData.Type.Bombership || unit.type == UnitData.Type.Scout)
                            __result *= 0.5f;
                    }
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-AI] Error in GetBuildUnitScore: {ex}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PathFinder), nameof(PathFinder.GetMoveOptions))]
        private static void GetMoveOptions_StopSuicide(GameState gameState, WorldCoordinates start, int maxCost, UnitState unit, ref Il2CppSystem.Collections.Generic.List<WorldCoordinates> __result)
        {
            try
            {
                if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                    return;

                if (!gameState.TryGetPlayer(unit.owner, out PlayerState player) || !player.AutoPlay)
                    return;

                // Only filter stiff units (or bombership)
                if (!unit.UnitData.HasAbility(UnitAbility.Type.Stiff) && unit.type != UnitData.Type.Bombership)
                    return;

                var dangerousTiles = GetDangerousTilesCached(gameState, player, start, maxCost + 6);

                var optionsCopy = __result.ToArray();
                foreach (WorldCoordinates option in optionsCopy)
                {
                    TileData target = gameState.Map.GetTile(option);

                    if (unit.UnitData.HasAbility(UnitAbility.Type.Stiff)
                        && unit.type != UnitData.Type.Juggernaut
                        && !unit.HasAbility(UnitAbility.Type.Infiltrate))
                    {
                        if (dangerousTiles.Contains(option))
                            __result.Remove(option);

                        if (!target.terrain.IsWater() && unit.type == UnitData.Type.Bombership)
                            __result.Remove(option);
                    }
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-AI] Error in GetMoveOptions: {ex}");
            }
        }

        // =========================================================================
        // Helpers
        // =========================================================================
        private static void EnsureCitadelCache(GameState gameState, PlayerState player)
        {
            if (citadelCacheTurn == gameState.CurrentTurn) return;

            cityCitadelCornerCache.Clear();
            foreach (TileData city in player.GetCityTiles(gameState))
            {
                if (city == null) continue;
                CityAnalysisResult? result = ForceScanCornerForCitadel(
                    gameState.Map, gameState, city, 5, false, player, Faction.Both, false);

                if (result?.TargetTile != null)
                    cityCitadelCornerCache[city.coordinates] = result.TargetTile;
            }
            citadelCacheTurn = (int)gameState.CurrentTurn;
        }

        private static HashSet<WorldCoordinates> GetDangerousTilesCached(GameState gameState, PlayerState player, WorldCoordinates center, int radius)
        {
            if (dangerousCacheTurn != gameState.CurrentTurn)
            {
                dangerousTilesCache.Clear();
                dangerousCacheTurn = (int)gameState.CurrentTurn;
            }

            if (dangerousTilesCache.TryGetValue(player.Id, out var cached))
                return cached;

            var set = GetDangerousTilesInArea(gameState, player, center, radius);
            dangerousTilesCache[player.Id] = set;
            return set;
        }

        private static bool IsWaterUnderMyControl(GameState gameState, TileData startTile, PlayerState player)
        {
            if (!startTile.IsWater) return false;

            var queue = new Queue<WorldCoordinates>();
            var visited = new HashSet<WorldCoordinates>();
            queue.Enqueue(startTile.coordinates);
            visited.Add(startTile.coordinates);

            int iterations = 0;
            const int maxIterations = 1500; // reduced from 3000

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

                    if (neighbor.improvement != null
                        && neighbor.improvement.type == ImprovementData.Type.City
                        && neighbor.owner != player.Id
                        && neighbor.owner != 0)
                        return false;

                    if (neighbor.IsWater && !visited.Contains(neighbor.coordinates))
                    {
                        visited.Add(neighbor.coordinates);
                        queue.Enqueue(neighbor.coordinates);
                    }
                }
            }
            return true;
        }

        private static HashSet<WorldCoordinates> GetDangerousTilesInArea(GameState gameState, PlayerState player, WorldCoordinates center, int radius)
        {
            var dangerousTiles = new HashSet<WorldCoordinates>();
            TileData[] nearbyTiles = MapDataExtensions.GetAreaSorted(gameState.Map, center, radius, true, true);

            for (int i = 0; i < nearbyTiles.Length; i++)
            {
                TileData enemyTile = nearbyTiles[i];
                if (enemyTile.unit == null || enemyTile.unit.owner == player.Id) continue;

                UnitState enemy = enemyTile.unit;
                int movementRange = UnitDataExtensions.GetMovement(enemy, gameState);
                int attackRange = UnitDataExtensions.GetRange(enemy.UnitData);
                int totalThreatRadius = enemy.HasAbility(UnitAbility.Type.Dash)
                    ? movementRange + attackRange
                    : Math.Max(movementRange, attackRange);

                TileData[] threatArea = MapDataExtensions.GetAreaSorted(gameState.Map, enemyTile.coordinates, totalThreatRadius, true, true);
                for (int j = 0; j < threatArea.Length; j++)
                    dangerousTiles.Add(threatArea[j].coordinates);
            }
            return dangerousTiles;
        }

        private static bool IsTileDangerous(GameState gameState, PlayerState player, WorldCoordinates targetCoord, int scanRadius)
        {
            TileData[] nearbyTiles = MapDataExtensions.GetAreaSorted(gameState.Map, targetCoord, scanRadius, true, true);

            for (int i = 0; i < nearbyTiles.Length; i++)
            {
                TileData enemyTile = nearbyTiles[i];
                if (enemyTile.unit == null || enemyTile.unit.owner == player.Id) continue;

                UnitState enemy = enemyTile.unit;
                int movementRange = UnitDataExtensions.GetMovement(enemy, gameState);
                int attackRange = UnitDataExtensions.GetRange(enemy.UnitData);
                int totalThreatRadius = enemy.HasAbility(UnitAbility.Type.Dash)
                    ? movementRange + attackRange
                    : Math.Max(movementRange, attackRange);

                int distance = Math.Max(
                    Math.Abs(enemyTile.coordinates.x - targetCoord.x),
                    Math.Abs(enemyTile.coordinates.y - targetCoord.y));

                if (distance <= totalThreatRadius) return true;
            }
            return false;
        }

        // =========================================================================
        // Forced Get Methods (cleaned)
        // =========================================================================
        private static Il2CppSystem.Collections.Generic.List<CommandBase> ForceGetBuildableImprovements(
            GameState gameState, PlayerState player, TileData tile, bool includeUnavailable = false)
        {
            var list = new Il2CppSystem.Collections.Generic.List<CommandBase>();
            if (player.Id != gameState.CurrentPlayer) return list;

            // Re-use existing GameLogicData – do NOT create a new one
            foreach (ImprovementData improvementData in gameState.GameLogicData.GetUnlockedImprovements(player))
            {
                if (improvementData.HasAbility(ImprovementAbility.Type.Manual)) continue;
                if (player.currency < improvementData.cost) continue;

                if (gameState.GameLogicData.MeetsRequirement(tile, improvementData, player, gameState)
                    && gameState.GameLogicData.MeetsAdjacencyRequirement(gameState.Map, tile, improvementData.adjacencyRequirements))
                {
                    CommandBase cmd = new BuildCommand(player.Id, improvementData.type, tile.coordinates);
                    if (includeUnavailable || cmd.IsValid(gameState))
                        list.Add(cmd);
                }
            }
            return list;
        }

        private static float ForceGetImprovementScore(GameState gameState, ImprovementData improvementData, TileData tileData, PlayerState player)
        {
            float num = 0f;
            int population = improvementData.rewards.GetPopulation();
            num += population * 25f;
            num += improvementData.rewards.GetCurrency() * 2f;
            num += improvementData.work * 20f;

            if (improvementData.HasAbility(ImprovementAbility.Type.Patina))
            {
                population = improvementData.growthRewards.GetPopulation();
                num += population * 100f;
            }

            UnitData unit = improvementData.creates.GetUnit();
            if (unit != null)
            {
                AI.UnitStats desired = AI.GetDesiredUnitStats(gameState, player, tileData);
                num += AI.GetBuildUnitScore(gameState, player, unit, desired, tileData);
            }

            if (improvementData.adjacencyImprovements != null && improvementData.adjacencyImprovements.Count > 0)
            {
                int adj = ActionUtils.GetAdjacencyBonusAt(gameState, tileData, improvementData);
                if (improvementData.adjacencyImprovements.Contains(ImprovementData.Type.PolarisClimate))
                    adj += player.aiState.frozenTileCount / 20;
                num += improvementData.growthRewards.GetPopulation() * 20 * adj;
                num += improvementData.work * 20 * adj;
            }

            if (tileData.rulingCityCoordinates != WorldCoordinates.NULL_COORDINATES)
            {
                TileData city = gameState.Map.GetTile(tileData.rulingCityCoordinates);
                if (city?.improvement != null
                    && city.improvement.type == ImprovementData.Type.City
                    && (int)city.improvement.xp + population > (int)city.improvement.level
                        - improvementData.CalculateImprovementPopulationAtLevel((int)tileData.improvement.level))
                {
                    num += 100f;
                }

                if (improvementData.IsRouteOpener() && city != null)
                {
                    bool hasRoute = false;
                    foreach (TileData t in gameState.Map.GetArea(city.coordinates, 1, true, false))
                    {
                        if (t.improvement != null
                            && gameState.GameLogicData.TryGetData(t.improvement.type, out ImprovementData d)
                            && d.IsRouteOpener())
                        {
                            hasRoute = true;
                            break;
                        }
                    }
                    if (!city.IsConnected && !hasRoute)
                    {
                        num += 30f;
                        if (city.coordinates == player.startTile) num += 50f;
                    }
                }
            }

            if (gameState.Settings.RulesGameMode == (GameMode)1)
                num += improvementData.rewards.GetScore() / 10f;

            num += AI.GetImprovementAbilityScore(gameState, player, improvementData, tileData);

            if (improvementData.type != ImprovementData.Type.Road
                && tileData.resource != null
                && !gameState.GameLogicData.IsResourceRequiredByImprovement(tileData.resource.type, improvementData))
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
            Faction findType = Faction.Both,
            bool findMost = true)
        {
            if (gameState == null || cityTile == null || map == null || currentOwner == null) return null;

            var territoryTiles = ActionUtils.GetCityAreaSorted(gameState, cityTile);
            if (territoryTiles == null || territoryTiles.Count == 0) return null;

            var startingPoints = new Dictionary<string, TileData>();

            if (searchFromCenter)
            {
                startingPoints["CityCenter"] = cityTile;
            }
            else
            {
                TileData? topLeft = null, topRight = null, bottomLeft = null, bottomRight = null;
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

                if (topLeft != null && MapDataExtensions.DistanceToEdge(map, topLeft.coordinates) != 0)
                    startingPoints["TopLeft"] = topLeft;
                if (topRight != null && MapDataExtensions.DistanceToEdge(map, topRight.coordinates) != 0)
                    startingPoints["TopRight"] = topRight;
                if (bottomLeft != null && MapDataExtensions.DistanceToEdge(map, bottomLeft.coordinates) != 0)
                    startingPoints["BottomLeft"] = bottomLeft;
                if (bottomRight != null && MapDataExtensions.DistanceToEdge(map, bottomRight.coordinates) != 0)
                    startingPoints["BottomRight"] = bottomRight;
            }

            CityAnalysisResult? bestResult = null;

            foreach (var kvp in startingPoints)
            {
                TileData startTile = kvp.Value;
                if (startTile == null) continue;

                WorldCoordinates centerCoord = new WorldCoordinates(startTile.coordinates.X, startTile.coordinates.Y);
                TileData[] areaTiles = map.GetAreaSorted(centerCoord, searchRadius, true, true);

                int enemyCityCount = 0;
                int ownedCityCount = 0;

                if (areaTiles != null)
                {
                    foreach (var areaTile in areaTiles)
                    {
                        if (areaTile?.improvement == null) continue;
                        if (areaTile.improvement.type != ImprovementData.Type.City) continue;
                        if (areaTile.coordinates.X == cityTile.coordinates.X
                            && areaTile.coordinates.Y == cityTile.coordinates.Y) continue;

                        if (areaTile.owner != currentOwner.Id) enemyCityCount++;
                        else ownedCityCount++;
                    }
                }

                var currentResult = new CityAnalysisResult
                {
                    TargetTile = startTile,
                    EnemyCityCount = enemyCityCount,
                    OwnedCityCount = ownedCityCount,
                    TileTypeLabel = kvp.Key
                };

                if (bestResult == null)
                {
                    bestResult = currentResult;
                    continue;
                }

                int currentCount = findType switch
                {
                    Faction.Enemy => currentResult.EnemyCityCount,
                    Faction.Owned => currentResult.OwnedCityCount,
                    Faction.Both => currentResult.EnemyCityCount + currentResult.OwnedCityCount,
                    _ => 0
                };

                int bestCount = findType switch
                {
                    Faction.Enemy => bestResult.EnemyCityCount,
                    Faction.Owned => bestResult.OwnedCityCount,
                    Faction.Both => bestResult.EnemyCityCount + bestResult.OwnedCityCount,
                    _ => 0
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
            return bestResult;
        }
    }
}