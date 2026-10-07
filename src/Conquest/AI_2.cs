using HarmonyLib;
using Il2CppSystem.Threading.Tasks;
using Polytopia.Data;
using PolytopiaBackendBase.Game;
using PolyMode;
using Il2CppSystem.Linq.Expressions.Interpreter;

namespace Conquest
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
                if (winningPlayer == null)
                {
                    __result = 0f;
                    return true;
                }

                if (gameState.Settings.RulesGameMode == EnumCache<GameMode>.GetType("conquest")
                    || gameState.Settings.RulesGameMode == EnumCache<GameMode>.GetType("reign"))
                {
                    float totalCities = Math.Max(0.1f, MapDataExtensions.CountCities(gameState));
                    float cityProgress = winningPlayer.cities / totalCities;
                    __result = Math.Min(1f, Math.Max(0f, cityProgress));
                    return false;
                }

                if (gameState.Settings.RulesGameMode == EnumCache<GameMode>.GetType("rusha"))
                {
                    float incomeProgress =
                        ResourceDataUtils.CalculateIncomeFor(gameState, winningPlayer.Id)
                        / (winningPlayer.cities * 15);
                    __result = Math.Min(1f, Math.Max(0f, incomeProgress));
                    return false;
                }
                else if (gameState.Settings.RulesGameMode == EnumCache<GameMode>.GetType("rushb"))
                {
                    var list = new Il2CppSystem.Collections.Generic.List<TileData>();
                    gameState.Map.GetPlayerCityTiles(winningPlayer.Id, list);
                    short maxUnit = 0;
                    foreach (TileData tileData in list)
                    {
                        if (tileData?.improvement == null) continue;
                        maxUnit += (short)((short)tileData.improvement.level + 1);
                    }

                    float denom = Math.Max(1, (int)maxUnit);
                    float militaryProgress = winningPlayer.CountUnits(gameState) / denom;
                    __result = Math.Min(1f, Math.Max(0f, militaryProgress));
                    return false;
                }
                else if (gameState.Settings.RulesGameMode == EnumCache<GameMode>.GetType("rushc"))
                {
                    var all = gameState.GameLogicData.GetAllTechForTribe(winningPlayer.tribe);
                    float denom = Math.Max(1, all?.Count ?? 1);
                    __result = Math.Min(1f, Math.Max(0f, Rush.AI_2.TechCount(winningPlayer) / denom));
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-AI] GetGameProgress: {ex.Message}");
                __result = 0f;
                return true;
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

            if (player.GetRelation(opponent.Id).FirstMeet < 0
                && opponent.GetRelation(player.Id).LastAttackTurn < 0)
                return;

            float cityAdvantage = opponent.GetCityAdvantage(gameState);

            float hate = cityAdvantage;
            var opinionState = new OpinionState();

            if (cityAdvantage > 0f)
            {
                opinionState.AddOpinion(hate * 2.2f, EnumCache<OpinionManager.Type>.GetType("obstinate"));
                opinionState.AddOpinion(hate * 0.7f, OpinionManager.Type.Winning);
            }
            else
            {
                opinionState.AddOpinion(-hate * 2.2f, OpinionManager.Type.Weak);
            }

            if (!__instance.Opinions.ContainsKey(opponent.Id))
                __instance.Opinions[opponent.Id] = new OpinionState();

            __instance.Opinions[opponent.Id].AddOpinion(
                opinionState.GetOpinion(EnumCache<OpinionManager.Type>.GetType("obstinate")) * -1f,
                EnumCache<OpinionManager.Type>.GetType("obstinate"));
            __instance.Opinions[opponent.Id].AddOpinion(
                opinionState.GetOpinion(OpinionManager.Type.Winning) * -1f,
                OpinionManager.Type.Winning);
            __instance.Opinions[opponent.Id].AddOpinion(
                opinionState.GetOpinion(OpinionManager.Type.Weak) * -1f,
                OpinionManager.Type.Weak);
        }

        /*[HarmonyPostfix]
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
            {
                __result += (float)(cityAdv * 0.5);
            }
        }*/

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
        [HarmonyPriority(Priority.Last)]
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

                if (centerResult != null && centerResult.EnemyCityCount >= 2 && centerResult.EnemyCityCount - centerResult.OwnedCityCount > 2)
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
                var centerResult = MapAnalysis.ScanCityFromCenter(gameState.Map, gameState, tile, 8, playerState);
                
                if (centerResult != null && centerResult.EnemyCityCount == 0)
                {   
                    __result = random.Next(0, 2) == 0 ? CityReward.CityWall : CityReward.Resources;
                }
                else
                {
                     __result = random.Next(0, 2) == 0 ? CityReward.CityWall : EnumCache<CityReward>.GetType("valhalla");
                }
            }
            else if (tile.improvement.level == 4)
            {
                var centerResult = MapAnalysis.ScanCityFromCenter(gameState.Map, gameState, tile, 8, playerState);

                if (centerResult != null && centerResult.EnemyCityCount == 0 && playerState.cities >= 4)
                {   
                    __result = EnumCache<CityReward>.GetType("taxreform");
                }
                else
                {  
                    /*if (gameState.Settings.RulesGameMode == EnumCache<GameMode>.GetType("conquest")
                        || gameState.Settings.RulesGameMode == EnumCache<GameMode>.GetType("reign"))
                    {
                        __result = CityReward.BorderGrowth;
                    }
                    else
                    {
                        __result = random.Next(0, 1) == 0
                        ? CityReward.BorderGrowth
                        : CityReward.PopulationGrowth;
                    }*/
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
            if (gameState == null || tileData == null || player == null || improvementData == null) return;

            if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                return;

            if (improvementData.type != EnumCache<ImprovementData.Type>.GetType("citadel")) return;

            try
            {
                TileData rulingCity = gameState.Map.GetTile(tileData.rulingCityCoordinates);
                if (rulingCity?.improvement == null) return;

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
                        {
                            score *= 0.5f;
                        }
                    }
                    else
                    {
                        score = 0.1f; // non-corner, non-island
                    }
                }

                if (rulingCity.improvement.level < 4)
                {
                    score = 0;
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

        /*[HarmonyPrefix]
        [HarmonyPatch(typeof(AI), nameof(AI.CheckForTechNeeds))]
        private static bool CheckForTechNeeds_FixWaterBias(
            GameState gameState,
            PlayerState player,
            Il2CppSystem.Collections.Generic.List<TileData> playerEmpire,
            Il2CppSystem.Collections.Generic.Dictionary<TechData.Type, int> neededTech)
        {
            try
            {
                if (gameState?.Settings == null || player == null || neededTech == null)
                {
                    return true;
                }

                if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return true;
                }

                //neededTech.Clear();

                int fieldForestCount = 0;
                int disconnectedCities = 0;
                var random = new System.Random();

                for (int i = 0; i < gameState.Map.Tiles.Length; i++)
                {
                    TileData tile = gameState.Map.Tiles[i];
                    if (tile == null || !tile.GetExplored(player.Id)) continue;

                    if (tile.owner == player.Id)
                    {
                        if (tile.HasImprovement(ImprovementData.Type.City) && !tile.IsConnected)
                        {
                            disconnectedCities++;
                        }

                        if (tile.terrain == TerrainData.Type.Field || tile.terrain == TerrainData.Type.Forest)
                        {
                            fieldForestCount++;
                        }
                    }

                    // Terrain the player cannot access yet → tech need
                    if (!tile.CanBeAccessedByPlayer(gameState, player))
                    {
                        TechData unlockTech = gameState.GameLogicData.GetTechThatUnlocks(tile.terrain);
                        if (unlockTech != null)
                        {
                            // Water: much lower pressure
                            int weight;
                            if (tile.IsWater)
                            {
                                weight = random.Next(0, 5) == 0 ? 1 : 0;
                            }
                            else
                            {
                                weight = 1;
                            }

                            if (weight > 0)
                            {
                                AI.AddTechNeed(neededTech, unlockTech.type, weight);
                            }
                        }
                    }

                    // Visible resource → freelance improvement tech
                    if (tile.resource != null && gameState.GameLogicData.IsResourceVisibleToPlayer(tile.resource.type, player, gameState))
                    {
                        var improvements = gameState.GameLogicData.GetImprovementForResource(tile.resource.type);
                        if (improvements == null) continue;

                        for (int j = 0; j < improvements.Count; j++)
                        {
                            ImprovementData improvementData = improvements[j];
                            if (improvementData == null) continue;
                            if (!improvementData.HasAbility(ImprovementAbility.Type.Freelance)) continue;
                            if (gameState.GameLogicData.IsUnlocked(improvementData.type, player)) continue;

                            TribeData tribeData = gameState.GameLogicData.GetTribeData(player.tribe);
                            TechData tech = gameState.GameLogicData.GetTechThatUnlocks(improvementData, tribeData);
                            if (tech != null)
                            {
                                AI.AddTechNeed(neededTech, tech.type, 5);
                            }
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
        }*/

        /*[HarmonyPostfix]
        [HarmonyPatch(typeof(AI), nameof(AI.AddPossibleRoadBuildingCommands))]
        private static void AddPossibleRoadBuildingCommands_Citadels(
            GameState gameState,
            PlayerState player,
            Il2CppSystem.Collections.Generic.List<TileData> cityTiles,
            Il2CppSystem.Collections.Generic.List<AI.ScoredCommand> possibleCommands)
        {
            try
            {
                if (gameState?.Map == null || player == null || !player.AutoPlay || possibleCommands == null) return;

                // Empire tiles only
                Il2CppSystem.Collections.Generic.List<TileData>? empireTiles = null;
                try
                {
                    if (player.aiState != null)
                    {
                        empireTiles = player.aiState.PlayerMapData.empireTiles;
                    }
                }
                catch { }

                if (empireTiles == null || empireTiles.Count == 0) return;

                // Owned citadels inside empire
                var citadels = new List<TileData>();
                for (int i = 0; i < empireTiles.Count; i++)
                {
                    TileData tile = empireTiles[i];
                    if (tile?.owner != player.Id || tile.improvement == null) continue;

                    try
                    {
                        if (tile.improvement.type == EnumCache<ImprovementData.Type>.GetType("citadel"))
                        {
                            citadels.Add(tile);
                        }
                    }
                    catch { }
                }

                if (citadels.Count == 0) return;

                TerrainData forest;
                TerrainData field;
                gameState.GameLogicData.TryGetData(TerrainData.Type.Forest, out forest);
                gameState.GameLogicData.TryGetData(TerrainData.Type.Field, out field);

                var terrains = new Il2CppSystem.Collections.Generic.List<TerrainData>();
                if (forest != null) terrains.Add(forest);
                if (field != null) terrains.Add(field);

                PathFinderSettings settings = PathFinderSettings.CreateDefault(player, terrains, gameState.Version, gameState);

                for (int i = 0; i < cityTiles.Count; i++)
                {
                    TileData city = cityTiles[i];
                    if (city == null) continue;

                    Il2CppSystem.Collections.Generic.List<WorldCoordinates>? bestRoute = null;
                    int bestLen = int.MaxValue;

                    for (int c = 0; c < citadels.Count; c++)
                    {
                        TileData citadel = citadels[c];
                        Il2CppSystem.Collections.Generic.List<WorldCoordinates> route = AI.GetRoute(gameState, city, citadel, settings);
                        if (route == null || route.Count == 0) continue;

                        for (int j = 0; j < route.Count; j++)
                        {
                            TileData routeTile = gameState.Map.GetTile(route[j]);
                            if (routeTile != null && routeTile.HasRoad)
                            {
                                route.RemoveAt(j--);
                            }
                        }

                        if (route.Count == 0) continue;

                        if (route.Count < bestLen)
                        {
                            bestLen = route.Count;
                            bestRoute = route;
                        }
                    }

                    if (bestRoute == null) continue;

                    float score = (float)gameState.GetCityPotential(city, player) / (float)Math.Max(1, bestRoute.Count);
                    score *= (float)0.5;

                    for (int k = 0; k < bestRoute.Count; k++)
                    {
                        TileData tile = gameState.Map.GetTile(bestRoute[k]);
                        if (tile == null) continue;

                        possibleCommands.Add(new AI.ScoredCommand
                        {
                            command = new BuildCommand(player.Id,ImprovementData.Type.Road, tile.coordinates),
                            score = score
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning($"[Conquest-AI] AddPossibleRoadBuildingCommands: {ex.Message}");
            }
        }*/

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapDataExtensions), nameof(MapDataExtensions.GetPlayerCityTiles))]
        private static void GetPlayerCityTiles_Citadel(MapData mapData, byte playerId, Il2CppSystem.Collections.Generic.List<TileData> cityTiles)
        {
            if (cityTiles == null)
            {
                cityTiles = new Il2CppSystem.Collections.Generic.List<TileData>();
            }
            cityTiles.Clear();
            foreach (TileData tileData in mapData.Tiles)
            {
                if (tileData.owner == playerId && (tileData.HasImprovement(ImprovementData.Type.City) || tileData.HasImprovement(EnumCache<ImprovementData.Type>.GetType("citadel"))))
                {
                    cityTiles.Add(tileData);
                }
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(TileData), "get_IsConnected")]
        private static bool IsConnected_Citadel_Prefix(TileData __instance, ref bool __result)
        {
            try
            {
                if (__instance?.improvement == null)
                    return true; // let vanilla warn / return false

                // Real city → vanilla
                if (__instance.improvement.type == ImprovementData.Type.City)
                    return true;

                var citadelType = EnumCache<ImprovementData.Type>.GetType("citadel");
                if (__instance.improvement.type != citadelType)
                    return true; // other non-city → vanilla warning (or handle below)

                // --- citadel: do not run vanilla ---
                if (__instance.owner == 0)
                {
                    __result = false;
                    return false;
                }

                bool isCapital = __instance.capitalOf == __instance.owner;
                bool linked =
                    __instance.improvement.connectedToCapitalOfPlayer == __instance.owner;

                if (!isCapital && !linked
                    && __instance.rulingCityCoordinates != WorldCoordinates.NULL_COORDINATES)
                {
                    var map = GameManager.GameState?.Map;
                    TileData city = map.GetTile(__instance.rulingCityCoordinates);
                    if (city != null
                        && city.HasImprovement(ImprovementData.Type.City)
                        && city.owner == __instance.owner
                        && city.improvement != null)
                    {
                        linked =
                            city.improvement.connectedToCapitalOfPlayer == __instance.owner
                            || city.capitalOf == __instance.owner;
                    }
                }

                __result = isCapital || linked;
                return false;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest] IsConnected prefix: {ex}");
                return true;
            }
        }

        // =========================================================================
        // C. Destroy
        // =========================================================================
        // Throttle: one destroy attempt per player per N turns
        static readonly Dictionary<byte, int> lastCitadelDestroyTurn = new Dictionary<byte, int>();
        const int CitadelDestroyCooldownTurns = 4;
        const int MinCityLevelForCitadelDestroy = 4;
        const int MinCurrencyForCitadelDestroy = 40;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(AI), nameof(AI.GetTileCommands))]
        private static void GetTileCommands_FreeCitadelCorner(
            GameState gameState,
            PlayerState player,
            CommandType specificCommand,
            ref CommandBase __result)
        {
            try
            {
                if (gameState?.Settings == null || player == null || !player.AutoPlay)
                    return;

                if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                    return;

                // Only when AI is looking for any command or Destroy
                if (specificCommand != CommandType.None && specificCommand != CommandType.Destroy)
                    return;

                // Don't steal a high-priority command if vanilla already picked something important
                if (__result != null)
                {
                    var t = __result.GetCommandType();
                    if (t == CommandType.Attack || t == CommandType.Train || t == CommandType.Research)
                        return;
                }

                if (player.currency < MinCurrencyForCitadelDestroy)
                    return;

                if (lastCitadelDestroyTurn.TryGetValue(player.Id, out int last)
                    && (int)gameState.CurrentTurn - last < CitadelDestroyCooldownTurns)
                    return;

                GetCitadelCache(gameState, player);
                if (cityCitadelCornerCache.Count == 0)
                    return;

                var citadelType = EnumCache<ImprovementData.Type>.GetType("citadel");
                DestroyCommand? best = null;

                foreach (var kv in cityCitadelCornerCache)
                {
                    TileData city = gameState.Map.GetTile(kv.Key);
                    TileData corner = kv.Value;
                    if (city?.improvement == null || corner == null)
                        continue;
                    if (city.owner != player.Id)
                        continue;
                    if (city.improvement.level < MinCityLevelForCitadelDestroy)
                        continue;

                    // Corner must be ours and blocked by a replaceable improvement
                    if (corner.owner != player.Id)
                        continue;
                    if (corner.improvement == null)
                        continue; // already empty — build path can use it

                    var imp = corner.improvement.type;
                    if (imp == ImprovementData.Type.City)
                        continue;
                    if (imp == citadelType)
                        continue;
                    if (imp == ImprovementData.Type.LightHouse)
                        continue;
                    try
                    {
                        if (imp.IsMonument())
                            continue;
                    }
                    catch { }

                    // Optional: don't clear high-pop producers if city is about to level
                    // (keep minimal — skip markets / high level)
                    if (imp == ImprovementData.Type.Market && corner.improvement.level > 2)
                        continue;

                    // Enemy unit on tile — skip
                    if (corner.unit != null && corner.unit.owner != player.Id)
                        continue;

                    var destroy = new DestroyCommand(player.Id, corner.coordinates);
                    if (!destroy.IsValid(gameState))
                        continue;

                    best = destroy;
                    break; // first valid corner is enough
                }

                if (best == null)
                    return;

                __result = best;
                lastCitadelDestroyTurn[player.Id] = (int)gameState.CurrentTurn;
                Loader.modLogger?.LogInfo(
                    $"[Conquest-AI] Free citadel corner destroy @ {best.Coordinates} p={player.Id} t={gameState.CurrentTurn}");
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-AI] FreeCitadelCorner: {ex.Message}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(DestroyCommand), nameof(DestroyCommand.Execute))]
        static void DestroyCommand_CitadelCooldown(DestroyCommand __instance, GameState state)
        {
            if (state == null || __instance == null) return;
            lastCitadelDestroyTurn[__instance.PlayerId] = (int)state.CurrentTurn;
        }

        static DestroyCommand? TryFindCitadelCornerDestroy(GameState gameState, PlayerState player)
        {
            if (player.currency < MinCurrencyForCitadelDestroy)
                return null;

            if (lastCitadelDestroyTurn.TryGetValue(player.Id, out int last)
                && (int)gameState.CurrentTurn - last < CitadelDestroyCooldownTurns)
                return null;

            GetCitadelCache(gameState, player);
            if (cityCitadelCornerCache.Count == 0)
                return null;

            var citadelType = EnumCache<ImprovementData.Type>.GetType("citadel");
            DestroyCommand? best = null;
            int bestUnclaimed = -1;

            foreach (var kv in cityCitadelCornerCache)
            {
                TileData city = gameState.Map.GetTile(kv.Key);
                TileData corner = kv.Value;
                if (city?.improvement == null || corner == null) continue;
                if (city.owner != player.Id) continue;
                if (city.improvement.level < MinCityLevelForCitadelDestroy) continue;
                if (city.improvement.type != ImprovementData.Type.City) continue; // skip citadel-as-city tiles

                if (corner.owner != player.Id) continue;
                if (corner.improvement == null) continue;

                var imp = corner.improvement.type;
                if (imp == ImprovementData.Type.City || imp == citadelType) continue;
                if (imp == ImprovementData.Type.LightHouse) continue;
                try { if (imp.IsMonument()) continue; } catch { }
                if (imp == ImprovementData.Type.Market && corner.improvement.level > 2) continue;
                if (corner.unit != null && corner.unit.owner != player.Id) continue;

                // Only free corners that still expand somewhere
                int border = Math.Max(1, (int)city.improvement.borderSize);
                int unclaimed = CountUnclaimedInRadius(gameState, corner.coordinates, border);
                if (unclaimed < 2) continue;

                var destroy = new DestroyCommand(player.Id, corner.coordinates);
                if (!destroy.IsValid(gameState)) continue;

                if (unclaimed > bestUnclaimed)
                {
                    bestUnclaimed = unclaimed;
                    best = destroy;
                }
            }

            return best;
        }

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
                if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
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

                GetCitadelCache(gameState, player);

                float bestScoreDifference = 0f;
                DestroyCommand? bestDestroyCommand = null;
                ImprovementData? bestOldType = null;
                ImprovementData? bestNewType = null;

                var citadelType = EnumCache<ImprovementData.Type>.GetType("citadel");

                foreach (TileData tileData in empireTiles)
                {
                    if (tileData == null || tileData.improvement == null) continue;
                    if (tileData.improvement.type == ImprovementData.Type.City || tileData.improvement.type == ImprovementData.Type.LightHouse || tileData.improvement.type == citadelType) continue;
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

                        if (currentData.type == citadelType)
                        {
                            TileData capital = gameState.Map.GetTile(tileData.rulingCityCoordinates);
                            if (capital?.improvement == null) continue;

                            int citadelCount = Main.CountCityCitadel(gameState, tileData);
                            if (Main.CityHasMaxCitadel(gameState, tileData, player, citadelCount)) continue;

                            bool isolatedIsland = MapAnalysis.IsIsolatedLandInWater(gameState, tileData);

                            bool isBestCorner =
                                cityCitadelCornerCache.TryGetValue(tileData.rulingCityCoordinates, out TileData? corner)
                                && corner != null
                                && tileData.coordinates.X == corner.coordinates.X
                                && tileData.coordinates.Y == corner.coordinates.Y;

                            // Only citadel on best corner OR isolated land in water
                            if (!isolatedIsland && !isBestCorner) continue;

                            int border = Math.Max(1, (int)capital.improvement.borderSize);
                            int unclaimed = 0;
                            TileData[] nearby = MapDataExtensions.GetAreaSorted(gameState.Map, tileData.coordinates, border, true, true);
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
                Loader.modLogger?.LogError($"[Conquest-AI] GetTileCommands_DestroyCmd: {ex}");
            }
        }*/

        // =========================================================================
        // D. Military
        // =========================================================================
        /*[HarmonyPrefix]
        [HarmonyPatch(typeof(AI), nameof(AI.AddUnitActionOptions))]
        private static bool AddUnitActionOptions_Rider(
            GameState gameState, 
            PlayerState player, 
            UnitState unit, 
            UnitData unitData, 
            TileData tile, 
            float currentSituationScore, 
            Il2CppSystem.Collections.Generic.List<AI.ScoredCommand> commands)
        {
            try
            {
                if (unit.type == UnitData.Type.Rider && unit.health <= unitData.health * 0.8)
                {
                    foreach (CommandBase commandBase in CommandUtils.GetUnitActions(gameState, player, tile, false))
                    {
                        if (commandBase.GetCommandType() == CommandType.Recover)
                        {
                            commands.Add(new AI.ScoredCommand
                            {
                                command = commandBase,
                                score = currentSituationScore + (float)ActionUtils.GetHealAmount(gameState, tile) * 0.1f + 10
                            });
                        }
                    }
                    return true;
                }
                else
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-AI] AddUnitActionOptions: {ex}");
                return true;
            }
        }*/

        [HarmonyPostfix]
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
                if (unit.UnitData.HasAbility(UnitAbility.Type.Escape) && unit.attacked && !(startTile.improvement != null && startTile.improvement.type == ImprovementData.Type.City && startTile.owner != unit.owner))
                {
                    //Loader.modLogger?.LogInfo($"[Conquest-AI] Escape has attacked");
                    List<WorldCoordinates> enemyPositions = MapAnalysis.CollectEnemyPositions(gameState, start, 7, player.Id);
                    //if (enemyPositions.Count == 0 || __result.Count == 0) return;
                    //Loader.modLogger?.LogInfo($"[Conquest-AI] Enemy count for escape not null. Finding retreat location.");

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

                    //Loader.modLogger?.LogInfo($"[Conquest-AI] Best escape location calculated");
                    
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
                Loader.modLogger?.LogError($"[Conquest-AI] GetMoveOptions_Combined: {ex}");
            }
        }

        // =========================================================================
        // Helpers — cache
        // =========================================================================
        public static void GetCitadelCache(GameState gameState, PlayerState player)
        {
            if (citadelCacheTurn == gameState.CurrentTurn) return;

            cityCitadelCornerCache.Clear();
            foreach (TileData city in player.GetCityTiles(gameState))
            {
                if (city == null) continue;
                CityAnalysisResult? result = ForceScanCornerForCitadel(gameState.Map, gameState, city, 5, player, Faction.Both, false);
                if (result?.TargetTile != null)
                {
                    cityCitadelCornerCache[city.coordinates] = result.TargetTile;
                }
            }
            citadelCacheTurn = (int)gameState.CurrentTurn;
        }

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
        public static CityAnalysisResult? ForceScanCornerForCitadel(
            MapData map,
            GameState gameState,
            TileData cityTile,
            int searchRadius,
            PlayerState currentOwner,
            Faction findType = Faction.Both,
            bool findMost = false)
        {
            return MapAnalysis.ScanCityForCorners(map, gameState, cityTile, searchRadius, currentOwner, findType, findMost, requireEmptyTile: false);
        }

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

        // =========================================================================
        // E. Budget
        // =========================================================================
        private static readonly Dictionary<long, int> TrainsThisTurn = new Dictionary<long, int>();

        private static long Key(byte playerId, int turn)
        {
            return ((long)playerId << 32) | (uint)turn;
        }

        private static long CoordKey(WorldCoordinates c)
        {
            return ((long)c.X << 32) | (uint)c.Y;
        }

        private const int ReweightTopN = 15;

        // -------------------------------------------------------------------------
        // 1) Reweight before vanilla picks best command
        // -------------------------------------------------------------------------
        [HarmonyPostfix]
        [HarmonyPatch(typeof(AI), nameof(AI.GetImprovementScore))]
        private static void GetImprovementScore_GrowForest(
            GameState gameState,
            ImprovementData improvementData,
            TileData tileData,
            PlayerState player,
            ref float __result)
        {
            if (improvementData == null || tileData == null || player == null)
                return;
            if (improvementData.type != ImprovementData.Type.GrowForest)
                return;
            if (__result > 1f)
                return; // already viable

            // Only empty owned field (typical grow target)
            if (tileData.owner != player.Id || tileData.improvement != null)
                return;
            if (tileData.terrain != TerrainData.Type.Field)
                return;

            // Optional: skip if tile already good for farm/windmill
            // ...

            // Enough to pass vanilla "num > 1" and compete a bit
            __result = 16f; // tune 15–20
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(AI), nameof(AI.PickBestPossibleCommand))]
        private static bool PickBestPossibleCommand_Budget(
            GameState gameState,
            Il2CppSystem.Collections.Generic.List<AI.ScoredCommand> possibleCommands,
            PlayerState player,
            ref CommandBase __result)
        {
            try
            {
                if (possibleCommands == null || possibleCommands.Count <= 0)
                    return true;
                if (gameState?.Settings == null || player == null)
                    return true;

                var mode = gameState.Settings.RulesGameMode;
                if (mode == GameMode.Perfection
                    || mode == GameMode.Domination
                    || mode == GameMode.Glory
                    || mode == GameMode.Might
                    || mode == GameMode.Sandbox
                    || mode == GameMode.Tutorial)
                {
                    return true;
                }

                int count = possibleCommands.Count;
                var managed = new List<AI.ScoredCommand>(count);
            
                for (int i = 0; i < count; i++)
                {
                    /*if (possibleCommands[i].command.GetCommandType() == CommandType.Build && possibleCommands[i].command.Cast<BuildCommand>().Type.IsTemple())
                    {
                        Loader.modLogger?.LogInfo($"[Conquest] Denied temple cmd");
                    }
                    else 
                    {
                        managed.Add(possibleCommands[i]);
                    }*/
                    managed.Add(possibleCommands[i]);
                }

                // After copying to managed, before top-N:
                for (int i = 0; i < managed.Count; i++)
                {
                    var sc = managed[i];
                    var cmd = sc.command;
                    if (cmd == null) continue;

                    if (cmd.GetCommandType() == CommandType.Build)
                    {
                        var bc = cmd.Cast<BuildCommand>();
                        if (bc != null)
                        {
                            if (bc.Type.IsTemple())
                            {
                                //Loader.modLogger?.LogInfo($"[Conquest] Temple suppressed");
                                sc.score = 0f; // or very negative
                                managed[i] = sc;
                            }

                            /*if (bc.Type == ImprovementData.Type.GrowForest)
                            {
                                Loader.modLogger?.LogInfo($"[Conquest] Grow Forest raised");
                                sc.score = 200f;
                                managed[i] = sc;
                            }*/
                        }
                    }
                }

                if (gameState.Settings.RulesGameMode == EnumCache<GameMode>.GetType("conquest")
                    || gameState.Settings.RulesGameMode == EnumCache<GameMode>.GetType("reign"))
                {
                    var destroy = TryFindCitadelCornerDestroy(gameState, player);
                    if (destroy != null)
                    {
                        managed.Add(new AI.ScoredCommand
                        {
                            command = destroy,
                            score = 180f // high enough to beat weak improves; tune 150–250
                        });
                    }
                }

                // Sort by vanilla score first — only reweight top N
                managed.Sort((a, b) => b.score.CompareTo(a.score));
                int reweightCount = Math.Min(ReweightTopN, managed.Count);

                bool hasTrain = false, hasResearch = false, hasImprove = false;
                bool hasRoad = false, hasDiplo = false;

                for (int i = 0; i < reweightCount; i++)
                {
                    CommandBase cmd = managed[i].command;
                    if (cmd.GetCommandType() == CommandType.Build)
                    {
                        //Loader.modLogger?.LogInfo($"[Conquest] Cmd {i} is {cmd?.TryCast<BuildCommand>()?.Type.GetDisplayName()}");
                    }

                    if (cmd == null) continue;
                    switch (Classify(gameState, cmd))
                    {
                        case CommandPool.Train: hasTrain = true; break;
                        case CommandPool.Research: hasResearch = true; break;
                        case CommandPool.Improve: hasImprove = true; break;
                        case CommandPool.Road: hasRoad = true; break;
                        case CommandPool.Diplomacy: hasDiplo = true; break;
                    }
                }

                float wTrain = hasTrain ? 0.30f : 0f;
                float wResearch = hasResearch ? 0.20f : 0f;
                float wImprove = hasImprove ? 0.35f : 0f;
                float wRoad = hasRoad ? 0.10f : 0f;
                float wDiplo = hasDiplo ? 0.05f : 0f;

                if (hasResearch && !HasAffordableResearch(gameState, player, managed, reweightCount))
                    wResearch = 0f;

                try
                {
                    if (player.aiState != null)
                    {
                        var pmd = player.aiState.PlayerMapData;
                        if (pmd.units != null && pmd.cityTiles != null)
                        {
                            int u = pmd.units.Count;
                            int c = Math.Max(1, pmd.cityTiles.Count);
                            if (u >= c * 2)
                            {
                                wTrain *= 0.65f;
                                wImprove *= 1.25f;
                                wRoad *= 1.15f;
                            }
                        }
                    }
                }
                catch { }

                float sum = wTrain + wResearch + wImprove + wRoad + wDiplo;
                if (sum <= 0.0001f)
                    return true;

                wTrain /= sum;
                wResearch /= sum;
                wImprove /= sum;
                wRoad /= sum;
                wDiplo /= sum;

                int currency = player.Currency;
                int reserve;
                if (currency <= 3)
                {
                    reserve = 0;
                }
                else
                {
                    float frac = currency >= 20 ? 0.10f : 0.0f;
                    if (gameState.Settings.RulesGameMode == EnumCache<GameMode>.GetType("rusha"))
                    {
                        frac = (float)(frac + Math.Pow(gameState.CurrentTurn / Math.MaxMagnitude(1, gameState.Settings.rules.TurnLimit - 1), 10));
                        //Loader.modLogger?.LogInfo($"[Rush-Budget] Reserved fraction is {frac}.");
                    }

                    reserve = Math.Max(0, (int)(currency * frac));
                    if (AnyOwnedCitySieged(gameState, player) && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("rusha"))
                        reserve = Math.Min(reserve, Math.Max(0, currency / 10));
                    //Loader.modLogger?.LogInfo($"[Rush-Budget] Reserved {reserve} stars.");
                }

                int spendable = Math.Max(0, currency - reserve);
                int trainBudget = (int)(spendable * wTrain);
                int researchBudget = (int)(spendable * wResearch);
                int improveBudget = (int)(spendable * wImprove);
                int roadBudget = (int)(spendable * wRoad);
                int diploBudget = (int)(spendable * wDiplo);

                int trainsThisTurn = GetTrainCount(player.Id, (int)gameState.CurrentTurn);

                // Per-pick caches (city / tile)
                var farmCache = new Dictionary<long, bool>(8);
                var siegeCache = new Dictionary<long, bool>(8);
                var portCountCache = new Dictionary<long, int>(8);
                var secondaryCache = new Dictionary<long, bool>(16);

            for (int i = 0; i < managed.Count; i++)
            {
                AI.ScoredCommand sc = managed[i];
                CommandBase cmd = sc.command;
                if (cmd == null) continue;

                CommandPool bucket = Classify(gameState, cmd);
                int cost = EstimateCommandCost(gameState, player, cmd);
                if (cost > spendable)
                {
                    // strict: cannot dip into reserve
                    sc.score = 0f;
                    managed[i] = sc;
                    continue;
                }
                float mult = 1f;
                bool inTop = i < reweightCount;

                switch (bucket)
                {
                    case CommandPool.Train:
                        if (!inTop) break; // leave vanilla score
                        mult *= BudgetMult(cost, trainBudget);
                        if (cost > spendable * 0.5f) mult *= 0.25f;
                        mult *= SaveUnitSpend(gameState, player, cmd);
                        if (trainsThisTurn >= 1) mult *= 0.40f;
                        if (trainsThisTurn >= 2) mult *= 0.25f;
                        break;

                    case CommandPool.Research:
                        if (!inTop) break;
                        mult *= BudgetMult(cost, researchBudget);
                        break;

                    case CommandPool.Road:
                        if (!inTop) break;
                        mult *= BudgetMult(cost, roadBudget);
                        break;

                    case CommandPool.Diplomacy:
                        if (!inTop) break;
                        mult *= BudgetMult(cost, diploBudget);
                        break;

                    case CommandPool.Improve:
                    {
                        // Budget share only for top N; hard rules for ALL improves
                        if (inTop)
                            mult *= BudgetMult(cost, improveBudget);

                            BuildCommand bc = cmd.Cast<BuildCommand>();
                            if (bc != null)
                            {
                                TileData? tile = null;
                                try { tile = gameState.Map.GetTile(bc.Coordinates); } catch { }

                                if (bc.Type == ImprovementData.Type.ClearForest)
                                {
                                    if (tile?.owner == player.Id)
                                    {
                                        mult *= (tile != null && CitySiegedCached(gameState, player, tile, siegeCache))
                                            ? 100f : 0f;
                                    }
                                }
                                else if (bc.Type == ImprovementData.Type.BurnForest)
                                {
                                    bool nextToSawmill = false;
                                    if (tile != null)
                                    {
                                        try
                                        {
                                            var neighbors = gameState.Map.GetTileNeighbors(tile.coordinates);
                                            for (int j = 0; j < neighbors.Count; j++)
                                            {
                                                TileData n = neighbors[j];
                                                if (n?.improvement != null
                                                    && n.improvement.type == ImprovementData.Type.Sawmill)
                                                {
                                                    nextToSawmill = true;
                                                    break;
                                                }
                                            }
                                        }
                                        catch { }
                                    }

                                    if (nextToSawmill)
                                        mult *= 0f;
                                    else
                                        mult *= (tile != null && CityWantsFarmsCached(gameState, player, tile, farmCache))
                                            ? 2f : 0f;
                                }
                                else if (bc.Type == ImprovementData.Type.GrowForest)
                                {
                                    bool forSecondary = false;
                                    bool forFarm = false;
                                    if (tile != null)
                                    {
                                        try
                                        {
                                            long tk = CoordKey(tile.coordinates);
                                            if (!secondaryCache.TryGetValue(tk, out forSecondary))
                                            {
                                                ImprovementData.Type[] secondaries =
                                                {
                                                    ImprovementData.Type.Windmill,
                                                    ImprovementData.Type.Forge,
                                                    ImprovementData.Type.Sawmill,
                                                    ImprovementData.Type.Market
                                                };

                                                for (int s = 0; s < secondaries.Length; s++)
                                                {
                                                    if (!gameState.GameLogicData.TryGetData(secondaries[s], out ImprovementData sec)
                                                        || sec == null)
                                                        continue;
                                                    if (gameState.GameLogicData.CanBuild(gameState, tile, player, sec))
                                                    {
                                                        forSecondary = true;
                                                        break;
                                                    }
                                                }
                                                secondaryCache[tk] = forSecondary;
                                            }

                                            if (tile.resource != null
                                                && tile.resource.type == ResourceData.Type.Crop)
                                            {
                                                forFarm = true;
                                            }
                                        }
                                        catch { }
                                    }
                                    if (tile != null)
                                    {
                                        mult *= (forSecondary || forFarm || CitySiegedCached(gameState, player, tile, siegeCache)) ? 0f : 1f;
                                    }
                                    if (mult > 0f && !inTop)
                                        mult *= 1.5f; // optional: help mid-list grows a bit
                                }
                                else if (bc.Type == ImprovementData.Type.Port)
                                {
                                    if (tile != null)
                                    {
                                        mult *= PortValueMultCached(gameState, player, tile, bc, portCountCache, siegeCache);
                                    }
                                }
                                else if (bc.Type.IsMonument() && gameState.Settings.RulesGameMode == EnumCache<GameMode>.GetType("rushc"))
                                {
                                    if (tile != null)
                                    {
                                        TileData cityTile = gameState.Map.GetTile(tile.coordinates);
                                        var centerResult = MapAnalysis.ScanCityFromCenter(gameState.Map, gameState, cityTile, 5, player);
                                        Loader.modLogger?.LogInfo($"[Rush-Tech] Pre Monument cmd {i} is {bc.Type.GetDisplayName()} and score is {sc.score}");
                                        Loader.modLogger?.LogInfo($"[Rush-Tech] MapAnalysis result is Enemy = {centerResult.EnemyCityCount} & Owned = {centerResult.OwnedCityCount}");
                                        mult *= (float)(1 - 0.20 * centerResult.EnemyCityCount + 0.05 * centerResult.OwnedCityCount);
                                        mult *= (float)(1 - 0.40 * Rush.AI_2.CountMonumentsInCity(gameState, cityTile));
                                        Loader.modLogger?.LogInfo($"[Rush-Tech] Monument count is {Rush.AI_2.CountMonumentsInCity(gameState, cityTile)}");
                                        Loader.modLogger?.LogInfo($"[Rush-Tech] Post Monument cmd {i} is {bc.Type.GetDisplayName()} and score is {sc.score * mult}");
                                    }
                                    
                                }
                                else
                                {
                                    bool temple = false;
                                    try
                                    {
                                        if (gameState.GameLogicData.TryGetData(bc.Type, out ImprovementData id)
                                            && id != null)
                                        {
                                            temple = bc.Type.IsTemple();
                                        }
                                    }
                                    catch { }

                                    if (temple && tile != null)
                                        mult *= 0f;
                                }
                            }
                            break;
                        }
                    }

                    sc.score *= mult;
                    // StupidFactor removed from per-command hot path (was expensive)
                    managed[i] = sc;
                }

                managed.Sort((a, b) => b.score.CompareTo(a.score));

                // Prefer reweighted top, then rest of list (vanilla scores, unweighted)
                for (int i = 0; i < managed.Count; i++)
                {
                    CommandBase cmd = managed[i].command;
                    float cmdScore = managed[i].score;
                    if (cmd != null && cmd.IsValid(gameState) && cmdScore > 0)
                    {
                        __result = cmd;
                        return false;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[AI-Budget] PickBest: {ex}");
                return true;
            }
        }

        // -------------------------------------------------------------------------
        // 2) Count trains actually issued
        // -------------------------------------------------------------------------
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TrainCommand), nameof(TrainCommand.Execute))]
        private static void TrainCommand_Count(TrainCommand __instance, GameState state)
        {
            try
            {
                if (state == null || __instance == null) return;
                if (!state.TryGetPlayer(__instance.PlayerId, out PlayerState p) || p == null) return;
                if (!p.AutoPlay) return;

                long k = Key(__instance.PlayerId, (int)state.CurrentTurn);
                TrainsThisTurn.TryGetValue(k, out int n);
                TrainsThisTurn[k] = n + 1;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[AI-Budget] TrainCount: {ex}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UpgradeCommand), nameof(UpgradeCommand.Execute))]
        private static void UpgradeCommand_Count(UpgradeCommand __instance, GameState state)
        {
            try
            {
                if (state == null || __instance == null) return;
                if (!state.TryGetPlayer(__instance.PlayerId, out PlayerState p) || p == null) return;
                if (!p.AutoPlay) return;

                long k = Key(__instance.PlayerId, (int)state.CurrentTurn);
                TrainsThisTurn.TryGetValue(k, out int n);
                TrainsThisTurn[k] = n + 1;
            }
            catch { }
        }

        // -------------------------------------------------------------------------
        // 3) Classifier && helpers
        // -------------------------------------------------------------------------
        private enum CommandPool
        {
            Other,
            Train,
            Research,
            Improve,
            Road,
            Diplomacy
        }

        private static CommandPool Classify(GameState gameState, CommandBase cmd)
        {
            CommandType t = cmd.GetCommandType();

            if (t == CommandType.Train || t == CommandType.Upgrade)
                return CommandPool.Train;
            if (t == CommandType.Research)
                return CommandPool.Research;
            if (t == CommandType.EstablishEmbassy || t == CommandType.PeaceTreaty || t == CommandType.BreakPeace)
                return CommandPool.Diplomacy;
            if (t == CommandType.Build)
                return IsRoadBuild(cmd) ? CommandPool.Road : CommandPool.Improve;

            return CommandPool.Other;
        }

        private static bool IsRoadBuild(CommandBase cmd)
        {
            BuildCommand b = cmd.Cast<BuildCommand>();
            return b != null && b.Type == ImprovementData.Type.Road;
        }

        private static float BudgetMult(int cost, int budget)
        {
            if (cost <= 0) return 1f;
            if (budget <= 0) return 0.1f;
            if (cost <= budget) return 1f;
            return Math.Max(0.01f, (float)budget / cost);
        }

        private static int GetTrainCount(byte playerId, int turn)
        {
            TrainsThisTurn.TryGetValue(Key(playerId, turn), out int n);
            return n;
        }

        private static bool HasAffordableResearch(
            GameState gameState,
            PlayerState player,
            List<AI.ScoredCommand> commands,
            int limit)
        {
            int currency = player.Currency;
            int n = Math.Min(limit, commands.Count);
            for (int i = 0; i < n; i++)
            {
                CommandBase cmd = commands[i].command;
                if (cmd == null || cmd.GetCommandType() != CommandType.Research)
                    continue;

                int cost = EstimateCommandCost(gameState, player, cmd);
                if (cost > 0 && cost <= currency)
                    return true;
            }
            return false;
        }

        private static float SaveUnitSpend(GameState gameState, PlayerState player, CommandBase cmd)
        {
            int cost = EstimateCommandCost(gameState, player, cmd);
            if (cost <= 0) return 1f;

            int currency = Math.Max(1, player.Currency);
            float unitNeed = 0f;
            try
            {
                if (player.aiState != null)
                    unitNeed = player.aiState.unitNeed;
            }
            catch { }

            float burden = (float)cost / currency;
            float bias = 1.2f - 0.5f * burden;
            if (bias < 0.4f) bias = 0.4f;
            if (bias > 1.25f) bias = 1.25f;

            if (unitNeed > 1f)
                bias += 0.15f * Math.Min(unitNeed, 3f);
            else if (unitNeed < 0f)
            {
                bias -= 0.1f * Math.Min(-unitNeed, 3f);
                if (burden > 0.5f)
                    bias *= 0.75f;
            }

            if (currency <= 5 && cost >= 5)
                bias *= 0.5f;

            if (bias < 0.35f) bias = 0.35f;
            if (bias > 1.4f) bias = 1.4f;
            return bias;
        }

        private static int EstimateCommandCost(GameState gameState, PlayerState player, CommandBase cmd)
        {
            try
            {
                CommandType t = cmd.GetCommandType();

                if (t == CommandType.Train)
                {
                    TrainCommand tc = cmd.Cast<TrainCommand>();
                    if (tc != null
                        && gameState.GameLogicData.TryGetData(tc.Type, out UnitData ud)
                        && ud != null)
                        return ud.cost;
                }

                if (t == CommandType.Upgrade)
                {
                    UpgradeCommand tu = cmd.Cast<UpgradeCommand>();
                    if (tu != null
                        && gameState.GameLogicData.TryGetData(tu.Type, out UnitData ud)
                        && ud != null)
                        return ud.cost;
                }

                if (t == CommandType.Research)
                {
                    ResearchCommand rc = cmd.Cast<ResearchCommand>();
                    if (rc != null
                        && gameState.GameLogicData.TryGetData(rc.Type, out TechData td)
                        && td != null)
                        return gameState.GameLogicData.GetTechPrice(td, player, gameState);
                }

                if (t == CommandType.Build)
                {
                    BuildCommand bc = cmd.Cast<BuildCommand>();
                    if (bc != null
                        && gameState.GameLogicData.TryGetData(bc.Type, out ImprovementData id)
                        && id != null)
                        return id.cost;
                }

                if (t == CommandType.EstablishEmbassy
                    && gameState.GameLogicData.DiplomacyData != null)
                {
                    return gameState.GameLogicData.DiplomacyData.embassyCost;
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[AI-Budget] EstimateCost: {ex}");
            }

            return 0;
        }

        private static bool CitySieged(GameState gameState, PlayerState player, TileData tile)
        {
            try
            {
                if (tile.rulingCityCoordinates == WorldCoordinates.NULL_COORDINATES)
                    return false;
                TileData city = gameState.Map.GetTile(tile.rulingCityCoordinates);
                if (city == null || city.owner != player.Id)
                    return false;
                if (city.unit != null && city.unit.owner != city.owner)
                    return true;
            }
            catch { }
            return false;
        }

        private static bool CitySiegedCached(
            GameState gameState,
            PlayerState player,
            TileData tile,
            Dictionary<long, bool> cache)
        {
            if (tile.rulingCityCoordinates == WorldCoordinates.NULL_COORDINATES)
                return false;
            long k = CoordKey(tile.rulingCityCoordinates);
            if (cache.TryGetValue(k, out bool v))
                return v;
            v = CitySieged(gameState, player, tile);
            cache[k] = v;
            return v;
        }

        private static bool AnyOwnedCitySieged(GameState gameState, PlayerState player)
        {
            try
            {
                if (player.aiState?.PlayerMapData.cityTiles == null)
                    return false;
                foreach (var city in player.aiState.PlayerMapData.cityTiles)
                {
                    if (CitySieged(gameState, player, city))
                        return true;
                }
            }
            catch { }
            return false;
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

        private static bool CityWantsFarms(GameState gameState, PlayerState player, TileData tile)
        {
            try
            {
                if (tile.rulingCityCoordinates == WorldCoordinates.NULL_COORDINATES)
                    return false;
                TileData city = gameState.Map.GetTile(tile.rulingCityCoordinates);
                if (city == null || city.owner != player.Id)
                    return false;

                int forest = 0, crop = 0, farm = 0;
                foreach (var tile2 in ActionUtils.GetCityAreaSorted(gameState, city))
                {
                    if (tile2.terrain == TerrainData.Type.Forest)
                        forest++;
                    if (tile2.resource != null && tile2.resource.type == ResourceData.Type.Crop)
                        crop++;
                    if (tile2.improvement != null && tile2.improvement.type == ImprovementData.Type.Farm)
                        farm++;
                }

                if (!(crop + farm > 2))
                    return false;
                return forest > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool CityWantsFarmsCached(
            GameState gameState,
            PlayerState player,
            TileData tile,
            Dictionary<long, bool> cache)
        {
            if (tile.rulingCityCoordinates == WorldCoordinates.NULL_COORDINATES)
                return false;
            long k = CoordKey(tile.rulingCityCoordinates);
            if (cache.TryGetValue(k, out bool v))
                return v;
            v = CityWantsFarms(gameState, player, tile);
            cache[k] = v;
            return v;
        }

        private static float PortValueMultCached(
            GameState gameState,
            PlayerState player,
            TileData tile,
            BuildCommand bc,
            Dictionary<long, int> portCountCache,
            Dictionary<long, bool> siegeCache)
        {
            try
            {
                if (tile == null || player == null || gameState?.GameLogicData == null)
                    return 0f;

                if (!gameState.GameLogicData.TryGetData(bc.Type, out ImprovementData data) || data == null)
                    return 0f;

                int existing = 0;
                if (tile.rulingCityCoordinates != WorldCoordinates.NULL_COORDINATES)
                {
                    long ck = CoordKey(tile.rulingCityCoordinates);
                    if (!portCountCache.TryGetValue(ck, out existing))
                    {
                        TileData city = gameState.Map.GetTile(tile.rulingCityCoordinates);
                        if (city != null)
                        {
                            var area = ActionUtils.GetCityAreaSorted(gameState, city);
                            if (area != null)
                            {
                                for (int i = 0; i < area.Count; i++)
                                {
                                    TileData tileData = area[i];
                                    if (tileData?.improvement != null
                                        && tileData.improvement.type == ImprovementData.Type.Port)
                                    {
                                        existing++;
                                    }
                                }
                            }
                        }
                        portCountCache[ck] = existing;
                    }
                }

                float value = existing == 0 ? 1.0f : 0.5f;
                value -= (float)(0.1 * existing);
                value = (float)Math.Max(0.1, value);

                int cost = Math.Max(1, data.cost);
                int currency = player.Currency;
                if (currency < cost * 2)
                    value *= 0.5f;

                if (!TileTouchesWater(gameState, tile))
                    value *= 0.5f;

                if (CitySiegedCached(gameState, player, tile, siegeCache))
                    value *= 0.25f;

                return value;
            }
            catch
            {
                return 0f;
            }
        }

        private static bool TileTouchesWater(GameState gameState, TileData tile)
        {
            try
            {
                if (tile.terrain.IsWater()) return true;
                var n = gameState.Map.GetTileNeighbors(tile.coordinates);
                if (n == null) return false;
                for (int i = 0; i < n.Count; i++)
                {
                    if (n[i] != null && n[i].terrain.IsWater())
                        return true;
                }
            }
            catch { }
            return false;
        }

    }
}