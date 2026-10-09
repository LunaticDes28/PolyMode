using HarmonyLib;
using PolytopiaBackendBase.Game;
using PolytopiaBackendBase.Common;
using Polytopia.Data;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine.EventSystems;
using PolyMode;

namespace Conquest
{
    public static class Main
    {
        // =========================================================================
        // A. GameMode Settings
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.Generate))]
        private static void Generate_SetGamemode(GameState state, MapGeneratorSettings settings)
        {
            try
            {
                bool isConquest = UI_2.IsConquestSelected;
                bool isReign = UI_2.IsReignSelected;

                if (GameManager.PreliminaryGameSettings.GameType == GameType.Matchmaking
                    || GameManager.PreliminaryGameSettings.GameType == GameType.Multiplayer)
                    return;

                if (isConquest
                    || GameManager.PreliminaryGameSettings.RulesGameMode
                        == EnumCache<GameMode>.GetType("conquest"))
                {
                    state.Settings.RulesGameMode = EnumCache<GameMode>.GetType("conquest");
                    state.Settings.rules.WinByExtermination = true;

                    Loader.modLogger?.LogInfo(
                        $"[Conquest-Map] RulesGameMode stamped as ID: {(int)state.Settings.RulesGameMode}");

                    UI_2.IsConquestSelected = false;
                    Loader.modLogger?.LogInfo(
                        $"[Conquest-Map] Flag IsConquestSelected is set {UI_2.IsConquestSelected}");
                }
                else if (isReign
                    || GameManager.PreliminaryGameSettings.RulesGameMode
                        == EnumCache<GameMode>.GetType("reign"))
                {
                    state.Settings.RulesGameMode = EnumCache<GameMode>.GetType("reign");
                    state.Settings.rules.WinByCapital = true;

                    Loader.modLogger?.LogInfo(
                        $"[Conquest-Map] RulesGameMode stamped as ID: {(int)state.Settings.RulesGameMode}");

                    UI_2.IsReignSelected = false;
                    Loader.modLogger?.LogInfo(
                        $"[Conquest-Map] Flag IsReignSelected is set {UI_2.IsReignSelected}");
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
                if (GameManager.PreliminaryGameSettings.RulesGameMode
                        != EnumCache<GameMode>.GetType("conquest")
                    && GameManager.PreliminaryGameSettings.RulesGameMode
                        != EnumCache<GameMode>.GetType("reign"))
                {
                    return true;
                }

                if (playerCount > 8)
                {
                    Loader.modLogger?.LogWarning(
                        $"[CapitalGenerator] players={playerCount} > 8 → vanilla");
                    return true;
                }

                int mapType = (int)GameManager.PreliminaryGameSettings.mapPreset;
                Loader.modLogger?.LogInfo(
                    $"[CapitalGenerator] mapType={mapType} players={playerCount}");

                if (mapType == 3)
                {
                    Loader.modLogger?.LogInfo("[CapitalGenerator] Executing vanilla logics...");
                    return true;
                }

                Loader.modLogger?.LogInfo(
                    $"[CapitalGenerator] Quadrants players={playerCount} width={width}");

                int grid = (playerCount <= 4) ? 2 : 4;
                int domainSize = width / grid;
                if (domainSize < 3)
                {
                    Loader.modLogger?.LogError(
                        $"[CapitalGenerator] domainSize={domainSize} too small → vanilla");
                    return true;
                }

                int remainder = width - domainSize * grid;

                List<int> availableDomains = new List<int>();
                if (grid == 2)
                {
                    for (int i = 0; i < 4; i++)
                        availableDomains.Add(i);
                }
                else
                {
                    for (int i = 0; i < 16; i++)
                    {
                        int domainX = i % grid;
                        int domainY = i / grid;
                        bool isEdge = domainX == 0 || domainX == 3 || domainY == 0 || domainY == 3;
                        bool isCorner =
                            (domainX == 0 || domainX == 3) && (domainY == 0 || domainY == 3);
                        if (isEdge && !isCorner)
                            availableDomains.Add(i);
                    }
                }

                Il2CppStructArray<int> probabilities = new Il2CppStructArray<int>(width * width);
                for (int j = 1; j < grid; j++)
                {
                    for (int k = 1; k < grid; k++)
                    {
                        int offsetX = Math.Min(remainder, Math.Max(1, Math.Min(remainder, k) - 1));
                        int offsetY = Math.Min(remainder, Math.Max(1, Math.Min(remainder, j) - 1));
                        int px = k * domainSize + offsetX;
                        int py = j * domainSize + offsetY;
                        __instance.AddDistanceToProbabilityTable(
                            probabilities, width, new WorldCoordinates(px - 1, py - 1), domainSize);
                    }
                }

                List<int> chosenDomains = new List<int>();
                List<int> capitalTileIndices = new List<int>();

                for (int p = 0; p < playerCount; p++)
                {
                    if (availableDomains.Count == 0)
                        break;

                    int bestDomain =
                        PickBestDomain(__instance.random, availableDomains, chosenDomains, grid);
                    availableDomains.Remove(bestDomain);
                    chosenDomains.Add(bestDomain);

                    WorldCoordinates domainCoord = WorldCoordinates.FromIndex(bestDomain, grid);
                    int offsetX =
                        Math.Min(remainder, Math.Max(1, Math.Min(remainder, domainCoord.X) - 1));
                    int offsetY =
                        Math.Min(remainder, Math.Max(1, Math.Min(remainder, domainCoord.Y) - 1));
                    int originX = domainCoord.X * domainSize + offsetX;
                    int originY = domainCoord.Y * domainSize + offsetY;

                    int margin = (domainSize == 3) ? 1 : 2;
                    int inset = 1;
                    int startX = Math.Max(margin, originX + inset);
                    int endX = Math.Min(width - margin, originX + domainSize - inset);
                    int startY = Math.Max(margin, originY + inset);
                    int endY = Math.Min(width - margin, originY + domainSize - inset);

                    if (startX > endX || startY > endY)
                    {
                        startX = Math.Max(0, originX);
                        endX = Math.Min(width - 1, originX + domainSize - 1);
                        startY = Math.Max(0, originY);
                        endY = Math.Min(width - 1, originY + domainSize - 1);
                    }

                    int maxProb = __instance.CalculateProbabilityInRange(
                        probabilities, width, startX, endX, startY, endY);

                    int tileIndex;
                    if (maxProb <= 0)
                    {
                        int cx = Math.Clamp(originX + domainSize / 2, 0, width - 1);
                        int cy = Math.Clamp(originY + domainSize / 2, 0, width - 1);
                        tileIndex = new WorldCoordinates(cx, cy).ToIndex(width);
                    }
                    else
                    {
                        int roll = __instance.random.Range(0, maxProb);
                        tileIndex = __instance.IndexForProbabilityValueInRange(
                            probabilities, width, roll, startX, endX, startY, endY);
                    }

                    capitalTileIndices.Add(tileIndex);
                    Loader.modLogger?.LogInfo(
                        $"[CapitalGenerator] P{p + 1} domain={bestDomain} tile={WorldCoordinates.FromIndex(tileIndex, width)}");
                }

                __result = new Il2CppSystem.Collections.Generic.List<int>();
                foreach (int idx in capitalTileIndices)
                    __result.Add(idx);

                return false;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[CapitalGenerator] {ex}");
                return true;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.TryAddCapitalToContinent))]
        private static bool TryAddCapitalToContinent_CoastPangea(
            MapGenerator __instance,
            GameState gameState,
            PlayerState player,
            WorldContinent targetContinent,
            MapData map,
            Il2CppSystem.Collections.Generic.List<TileData> capitals,
            ref bool __result)
        {
            try
            {
                if (GameManager.PreliminaryGameSettings.RulesGameMode
                        != EnumCache<GameMode>.GetType("conquest")
                    && GameManager.PreliminaryGameSettings.RulesGameMode
                        != EnumCache<GameMode>.GetType("reign"))
                {
                    return true;
                }

                int mapType = -1;
                try { mapType = (int)GameManager.PreliminaryGameSettings.mapPreset; }
                catch { }

                if (mapType != 6) return true;
                if (map == null || player == null || targetContinent == null || capitals == null)
                    return true;

                WorldCoordinates preferredCoords = __instance.GetBestCityCoordinates(
                    gameState, map, targetContinent.Tiles, capitals);

                if (preferredCoords == WorldCoordinates.NULL_COORDINATES)
                {
                    if (targetContinent.Tiles != null && targetContinent.Tiles.Count > 0)
                        preferredCoords = targetContinent.Tiles[0];
                    else
                        return true;
                }

                TileData preferredTile = map.GetTile(preferredCoords);
                TileData? bestCoast = FindPangeaCoastTile(map, preferredTile, capitals);

                if (bestCoast != null)
                {
                    Loader.modLogger?.LogInfo(
                        $"[CapitalGenerator] Pangea P{player.Id}: " +
                        $"{preferredTile.coordinates} → coast {bestCoast.coordinates}");
                    preferredTile = bestCoast;
                }
                else
                {
                    Loader.modLogger?.LogWarning(
                        $"[CapitalGenerator] Pangea P{player.Id}: no coast tile, keep {preferredTile.coordinates}");
                }

                preferredTile.owner = player.Id;
                capitals.Add(preferredTile);
                __result = true;
                return false;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError(
                    $"[CapitalGenerator] TryAddCapitalToContinent Error: {ex}");
                return true;
            }
        }

        private static TileData? FindPangeaCoastTile(
            MapData map,
            TileData preferred,
            Il2CppSystem.Collections.Generic.List<TileData> currentCapitals)
        {
            List<TileData> coast = new List<TileData>();

            for (int i = 0; i < map.Tiles.Length; i++)
            {
                TileData tile = map.Tiles[i];
                if (tile == null || tile.IsWater) continue;
                if (tile.improvement != null) continue;

                bool nearWater = false;
                var neighbors = map.GetTileNeighbors(tile.coordinates);
                if (neighbors == null) continue;

                for (int n = 0; n < neighbors.Count; n++)
                {
                    var neighbor = neighbors[n];
                    if (neighbor != null && neighbor.IsWater)
                    {
                        nearWater = true;
                        break;
                    }
                }
                if (!nearWater) continue;
                coast.Add(tile);
            }

            if (coast.Count == 0) return null;

            TileData? best = null;
            int bestScore = int.MinValue;

            for (int i = 0; i < coast.Count; i++)
            {
                TileData tile2 = coast[i];
                int minDist = int.MaxValue;

                if (currentCapitals.Count == 0)
                {
                    minDist = MapDataExtensions.ChebyshevDistance(
                        tile2.coordinates, preferred.coordinates);
                    int score = 1000 - minDist;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = tile2;
                    }
                    continue;
                }

                for (int a = 0; a < currentCapitals.Count; a++)
                {
                    int dist = MapDataExtensions.ChebyshevDistance(
                        tile2.coordinates, currentCapitals[a].coordinates);
                    if (dist < minDist) minDist = dist;
                }

                int bias = MapDataExtensions.ChebyshevDistance(
                    tile2.coordinates, preferred.coordinates);
                int score2 = (minDist * 10) - bias;

                if (score2 > bestScore)
                {
                    bestScore = score2;
                    best = tile2;
                }
            }

            return best;
        }

        private static int PickBestDomain(
            Il2CppSystem.Random random,
            List<int> availableDomains,
            List<int> chosenDomainIndices,
            int grid)
        {
            if (chosenDomainIndices.Count == 0)
                return availableDomains[random.Range(0, availableDomains.Count)];

            int bestMinDist = -1;
            List<int> tied = new List<int>();

            for (int i = 0; i < availableDomains.Count; i++)
            {
                int candidate = availableDomains[i];
                int cx = candidate % grid;
                int cy = candidate / grid;

                int minDist = int.MaxValue;
                for (int j = 0; j < chosenDomainIndices.Count; j++)
                {
                    int other = chosenDomainIndices[j];
                    int ox = other % grid;
                    int oy = other / grid;
                    int dist = Math.Max(Math.Abs(cx - ox), Math.Abs(cy - oy));
                    if (dist < minDist)
                        minDist = dist;
                }

                if (minDist > bestMinDist)
                {
                    bestMinDist = minDist;
                    tied.Clear();
                    tied.Add(candidate);
                }
                else if (minDist == bestMinDist)
                {
                    tied.Add(candidate);
                }
            }

            return tied[random.Range(0, tied.Count)];
        }

        // =========================================================================
        // C. Village Generation Logics
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateInternal))]
        private static void GenerateInternal_DistributeVillages(
            MapGenerator __instance,
            GameState gameState,
            MapGeneratorSettings settings)
        {
            try
            {
                if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return;
                }

                ConquestVillageGeneration(__instance, gameState, settings);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Map] Village gen: {ex}");
            }
        }

        private static void ConquestVillageGeneration(
            MapGenerator gen,
            GameState gameState,
            MapGeneratorSettings settings)
        {
            List<TileData> neutralVillages = new List<TileData>();
            for (int i = 0; i < gameState.Map.Tiles.Length; i++)
            {
                TileData tile = gameState.Map.Tiles[i];
                if (tile.HasImprovement(ImprovementData.Type.City) && tile.owner == 0)
                    neutralVillages.Add(tile);
            }

            int playerCount = gameState.PlayerCount;
            if (playerCount <= 0)
                return;

            Loader.modLogger?.LogInfo(
                $"[Conquest-Map] {neutralVillages.Count} neutral villages for {playerCount} players");

            int remainder = neutralVillages.Count % playerCount;
            int citiesToSpawn = (remainder == 0) ? 0 : (playerCount - remainder);
            if (remainder > 0 && remainder >= playerCount * 0.5f && citiesToSpawn > 0)
            {
                Loader.modLogger?.LogInfo(
                    $"[Conquest-Map] Emergency placement attempted: need {citiesToSpawn} (remainder={remainder})");
                for (int s = 0; s < citiesToSpawn; s++)
                {
                    WorldCoordinates coords = gen.GetEmergencyCityPosition(gameState, gameState.Map);
                    if (coords == WorldCoordinates.NULL_COORDINATES)
                    {
                        Loader.modLogger?.LogInfo(
                            "[Conquest-Map] Emergency placement failed: attempt terminated");
                        break;
                    }
                    TileData target = gameState.Map.GetTile(coords);
                    if (target == null || target.improvement != null)
                        continue;
                    gen.SetTileAsCity(target);
                    AddEmergencyResources(gameState, target);
                    neutralVillages.Add(target);
                    Loader.modLogger?.LogInfo($"[Conquest-Map] Emergency city at {coords}");
                }
                gen.MakeOcean(gameState.Map, gameState, settings.shallowPercentOfWater == 0f);
            }

            int maxCitiesPerPlayer = neutralVillages.Count / playerCount;
            HashSet<WorldCoordinates> kept = new HashSet<WorldCoordinates>();
            var ownedByPlayer = new Dictionary<byte, List<WorldCoordinates>>();
            for (int p = 0; p < playerCount; p++)
                ownedByPlayer[gameState.PlayerStates[p].Id] = new List<WorldCoordinates>();

            for (int round = 0; round < maxCitiesPerPlayer; round++)
            {
                for (int p = 0; p < playerCount; p++)
                {
                    PlayerState player = gameState.PlayerStates[p];
                    List<WorldCoordinates> owned = ownedByPlayer[player.Id];

                    TileData? picked =
                        FindBestVillageForPlayer(gameState, neutralVillages, kept, player, owned);
                    if (picked == null) continue;

                    kept.Add(picked.coordinates);
                    owned.Add(picked.coordinates);
                }
            }

            int ruinsCount = 0;
            for (int i = 0; i < neutralVillages.Count; i++)
            {
                TileData village = neutralVillages[i];
                if (kept.Contains(village.coordinates)) continue;

                bool isWaterCity = true;
                foreach (TileData neighbour in
                    gameState.Map.GetTileNeighborsSorted(village.coordinates))
                {
                    if (!neighbour.terrain.IsWater())
                    {
                        isWaterCity = false;
                        break;
                    }
                }

                village.improvement = new ImprovementState
                {
                    type = ImprovementData.Type.Ruin,
                    borderSize = 0,
                    level = 1,
                    production = 1,
                    founded = 0
                };
                ruinsCount++;

                if (isWaterCity)
                    village.terrain = TerrainData.Type.Mountain;
            }

            Loader.modLogger?.LogInfo(
                $"[Conquest-Map] Gen done. cities={kept.Count}, ruins={ruinsCount}");
        }

        private static void AddEmergencyResources(GameState gameState, TileData cityTile)
        {
            var neighbors = gameState.Map.GetArea(cityTile.coordinates, 1, true, false);
            if (neighbors == null)
                return;
            int fish = 0;
            int fruit = 0;
            foreach (TileData n in neighbors)
            {
                if (n == null || n.coordinates.Equals(cityTile.coordinates))
                    continue;
                if (n.resource != null || n.improvement != null)
                    continue;
                if (fish < 2 && n.IsWater)
                {
                    n.resource = new ResourceState { type = ResourceData.Type.Fish };
                    fish++;
                }
                else if (fruit < 1 && !n.IsWater && n.terrain != TerrainData.Type.Mountain)
                {
                    n.resource = new ResourceState { type = ResourceData.Type.Fruit };
                    fruit++;
                }
                if (fish >= 2 && fruit >= 1)
                    break;
            }
        }

        private static TileData? FindBestVillageForPlayer(
            GameState gameState,
            List<TileData> neutralVillages,
            HashSet<WorldCoordinates> alreadyTaken,
            PlayerState player,
            List<WorldCoordinates> playerOwnedCoords)
        {
            WorldCoordinates capital = player.startTile;

            int cx = capital.X;
            int cy = capital.Y;
            int n = 1;
            if (playerOwnedCoords != null)
            {
                for (int i = 0; i < playerOwnedCoords.Count; i++)
                {
                    cx += playerOwnedCoords[i].X;
                    cy += playerOwnedCoords[i].Y;
                    n++;
                }
            }
            WorldCoordinates centroid = new WorldCoordinates(cx / n, cy / n);

            TileData? best = null;
            float bestScore = float.MaxValue;

            for (int i = 0; i < neutralVillages.Count; i++)
            {
                TileData village = neutralVillages[i];
                if (village == null) continue;
                if (alreadyTaken.Contains(village.coordinates)) continue;
                if (village.improvement == null
                    || village.improvement.type != ImprovementData.Type.City)
                    continue;

                int distCapital =
                    MapDataExtensions.ChebyshevDistance(village.coordinates, capital);
                int distCentroid =
                    MapDataExtensions.ChebyshevDistance(village.coordinates, centroid);

                float voronoiPenalty = 0;
                for (int p = 0; p < gameState.PlayerStates.Count; p++)
                {
                    PlayerState other = gameState.PlayerStates[p];
                    if (other == null || other.Id == 255 || other.Id == player.Id) continue;
                    int distOther =
                        MapDataExtensions.ChebyshevDistance(village.coordinates, other.startTile);
                    if (distOther < distCapital)
                        voronoiPenalty += (float)((distCapital - distOther) * 25);
                }

                float score = distCapital + 2.5f * distCentroid + voronoiPenalty;

                if (score < bestScore)
                {
                    bestScore = score;
                    best = village;
                }
            }

            return best;
        }

        // =========================================================================
        // D. City Distribution & Initialization
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(StartMatchAction), nameof(StartMatchAction.ExecuteDefault))]
        private static void StartMatchAction_InitializeVillages(
            StartMatchAction __instance,
            GameState gameState)
        {
            if (gameState?.Settings == null)
                return;
            try
            {
                if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return;
                }

                if (gameState.Settings.mapPreset == (MapPreset)6)
                {
                    foreach (TileData tile in gameState.Map.tiles)
                    {
                        if (gameState.TileIsCapitalOfPlayer(tile.coordinates) != 0)
                        {
                            foreach (TileData? tile2 in
                                gameState.Map.GetTileNeighborsSorted(tile.coordinates))
                            {
                                if (tile2.improvement != null
                                    && tile2.improvement.type == ImprovementData.Type.City)
                                {
                                    tile2.improvement = null;
                                }
                            }
                        }
                    }
                }

                Loader.modLogger?.LogInfo("[Conquest-Match] Village distribution + init...");
                ConquestVillageDistribution(gameState);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Match] StartMatch: {ex}");
            }
        }

        private static void ConquestVillageDistribution(GameState gameState)
        {
            List<TileData> neutralVillages = new List<TileData>();
            for (int i = 0; i < gameState.Map.Tiles.Length; i++)
            {
                TileData tile = gameState.Map.Tiles[i];
                if (tile.HasImprovement(ImprovementData.Type.City) && tile.owner == 0)
                    neutralVillages.Add(tile);
            }

            int playerCount = gameState.PlayerCount;
            if (playerCount <= 0) return;

            int maxCitiesPerPlayer = neutralVillages.Count / playerCount;
            HashSet<WorldCoordinates> assigned = new HashSet<WorldCoordinates>();
            var ownedByPlayer = new Dictionary<byte, List<WorldCoordinates>>();
            for (int p = 0; p < playerCount; p++)
                ownedByPlayer[gameState.PlayerStates[p].Id] = new List<WorldCoordinates>();

            Loader.modLogger?.LogInfo(
                $"[Conquest-Match] {neutralVillages.Count} neutrals → {maxCitiesPerPlayer} per player");

            for (int round = 0; round < maxCitiesPerPlayer; round++)
            {
                for (int p = 0; p < playerCount; p++)
                {
                    PlayerState player = gameState.PlayerStates[p];
                    List<WorldCoordinates> owned = ownedByPlayer[player.Id];
                    TileData? village =
                        FindBestVillageForPlayer(gameState, neutralVillages, assigned, player, owned);
                    if (village == null) continue;
                    assigned.Add(village.coordinates);
                    owned.Add(village.coordinates);
                    ConquestInitializeCity(gameState, village, player);
                }
            }

            foreach (TileData tile2 in gameState.Map.tiles)
            {
                if (tile2.improvement != null
                    && tile2.improvement.type == ImprovementData.Type.City
                    && tile2.owner == 0)
                {
                    tile2.improvement = null;
                }
            }

            Loader.modLogger?.LogInfo("[Conquest-Match] All cities initialized");
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
                    string name = MapDataExtensions.GenerateCityName(
                        state, tile.coordinates, tribeData, player.skinType);
                    if (tile.improvement != null)
                        tile.improvement.name = name;
                }
                player.cities++;
                UnitData unitData;
                if (state.GameLogicData.TryGetData(UnitData.Type.Warrior, out unitData))
                {
                    UnitState unit = ActionUtils.TrainUnitScored(state, player, tile, unitData);
                    unit.attacked = false;
                    unit.moved = false;
                }
                var cityArea = ActionUtils.GetCityAreaSorted(state, tile);
                if (cityArea != null)
                {
                    for (int j = 0; j < cityArea.Count; j++)
                    {
                        TileData territory = cityArea[j];
                        if (territory == null)
                            continue;
                        territory.owner = player.Id;
                        territory.rulingCityCoordinates = tile.coordinates;
                    }
                }
                ActionUtils.RuleArea(state, player, tile, true);
                ActionUtils.ExploreFromTile(state, player, tile, 2, true);
                InvalidateCityCaches();
                Loader.modLogger?.LogInfo(
                    $"[Conquest-Match] City for P{player.Id} at {tile.coordinates}");
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Match] Init city failed: {ex}");
            }
        }

        // =========================================================================
        // E. Citadel Logics (general)
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameLogicData), nameof(GameLogicData.CanBuild))]
        private static void CanBuild_Citadel(
            GameLogicData __instance,
            GameState gameState,
            TileData tile,
            PlayerState playerState,
            ImprovementData improvement,
            ref bool __result)
        {
            if (tile == null || playerState == null || improvement == null || gameState == null)
                return;

            if (tile.improvement != null && improvement.type != ImprovementData.Type.Road)
            {
                __result = false;
                return;
            }

            if (tile.unit != null)
            {
                if (tile.unit.owner != playerState.Id)
                {
                    if (gameState.TryGetPlayer(tile.unit.owner, out PlayerState unitOwner)
                        && unitOwner != null)
                    {
                        if (!unitOwner.HasPeaceWith(playerState.Id))
                        {
                            __result = false;
                            return;
                        }
                    }
                }
            }

            if (tile.rulingCityCoordinates != WorldCoordinates.NULL_COORDINATES)
            {
                if (improvement.HasAbility(ImprovementAbility.Type.Limited)
                    && __instance.HasImprovementWithinCityBorders(
                        gameState.Map, tile.rulingCityCoordinates, improvement.type))
                {
                    __result = false;
                    return;
                }
            }

            try
            {
                if (gameState.Settings?.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && gameState.Settings?.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return;
                }

                var citadelType = EnumCache<ImprovementData.Type>.GetType("citadel");

                // AI corner reservation — only when evaluating citadel/road (not every CanBuild)
                if (playerState.AutoPlay
                    && tile.rulingCityCoordinates != WorldCoordinates.NULL_COORDINATES
                    && (improvement.type == citadelType
                        || improvement.type == ImprovementData.Type.Road))
                {
                    TileData? rulingCity = gameState.Map.GetTile(tile.rulingCityCoordinates);
                    if (rulingCity != null)
                    {
                        AI_2.GetCitadelCache(gameState, playerState);

                        if (AI_2.cityCitadelCornerCache != null
                            && AI_2.cityCitadelCornerCache.TryGetValue(
                                rulingCity.coordinates, out TileData? targetedCorner)
                            && targetedCorner != null
                            && tile.coordinates.X == targetedCorner.coordinates.X
                            && tile.coordinates.Y == targetedCorner.coordinates.Y)
                        {
                            if (improvement.type != citadelType
                                && improvement.type != ImprovementData.Type.Road)
                            {
                                __result = false;
                                return;
                            }
                        }
                    }
                }

                if (improvement.type == citadelType && tile.owner == playerState.Id)
                {
                    int citadelCount =
                        CountCityCitadel(gameState, tile, out bool builtThisTurn);
                    if (CityHasMaxCitadel(gameState, tile, playerState, citadelCount)
                        || builtThisTurn)
                    {
                        __result = false;
                        return;
                    }
                    __result = true;
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest] Error in CanBuild Postfix: {ex}");
            }
        }

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
                if (tile != null
                    && gameState.GameLogicData.TryGetData(__instance.Type, out improvementData)
                    && gameState.TryGetPlayer(__instance.PlayerId, out playerState))
                {
                    if (improvementData.type != EnumCache<ImprovementData.Type>.GetType("citadel"))
                        return;

                    if (!tile.terrain.IsWater())
                    {
                        gameState.ActionStack.Add(
                            new BuildRoadAction(__instance.PlayerId, __instance.Coordinates));
                        gameState.ActionStack.Add(new UpdateRoutesAction(__instance.PlayerId));
                        var playerIdList = new Il2CppSystem.Collections.Generic.List<byte>();
                        playerIdList.Add(__instance.PlayerId);
                        gameState.ActionStack.Add(
                            new UpdateCityConnectionsAction(__instance.PlayerId, playerIdList));
                    }

                    TileData cityTile =
                        GameManager.GameState.Map.GetTile(tile.rulingCityCoordinates);
                    int area = cityTile.improvement.borderSize;
                    ActionUtils.ExploreFromTile(gameState, playerState, tile, area, true);

                    TileData[] areaSorted =
                        gameState.Map.GetAreaSorted(tile.coordinates, area, true, true);
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
                                    instance.Render();
                            }
                        }

                        foreach (TileData tileData in areaSorted)
                        {
                            Tile instance = tileData.GetInstance();
                            if (instance != null)
                                instance.Render();
                        }

                        ReactionUtils.UpdateSurroundingBordersAndTransportPaths(
                            playerState.Id, tile);
                        InvalidateCityCaches();
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
        private static bool DestroyImprovementAction_Citadel(
            DestroyImprovementAction __instance, GameState state)
        {
            try
            {
                if (state.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && state.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return true;
                }

                TileData tile = state.Map.GetTile(__instance.Coordinates);

                if (tile.improvement != null
                    && tile.improvement.type != EnumCache<ImprovementData.Type>.GetType("citadel"))
                {
                    return true;
                }

                TileData cityTile =
                    GameManager.GameState.Map.GetTile(tile.rulingCityCoordinates);
                int area = cityTile.improvement.borderSize;
                TileData[] areaSorted =
                    state.Map.GetAreaSorted(tile.coordinates, area, true, true);

                if (areaSorted != null && areaSorted.Length > 0)
                {
                    foreach (TileData tileData in areaSorted)
                    {
                        if (tileData.owner == cityTile.owner
                            && tileData.rulingCityCoordinates == cityTile.coordinates)
                        {
                            TileData[] areaSorted2 =
                                state.Map.GetAreaSorted(tileData.coordinates, area, true, true);
                            if (areaSorted2 == null) continue;

                            bool isRule = false;
                            var citadelType = EnumCache<ImprovementData.Type>.GetType("citadel");

                            foreach (TileData tileData2 in areaSorted2)
                            {
                                if (tileData2.improvement != null
                                    && (tileData2.improvement.type == ImprovementData.Type.City
                                        || tileData2.improvement.type == citadelType)
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
                                    num += state.CalculateImprovementScore(tileData);
                                state.ActionStack.Add(
                                    new DecreaseScoreAction(tileData.owner, num));

                                ImprovementData improvementData;
                                if (tileData.improvement != null
                                    && state.GameLogicData.TryGetData(
                                        tileData.improvement.type, out improvementData))
                                {
                                    int num2 =
                                        improvementData.CalculateImprovementPopulationAtLevel(
                                            tileData.improvement.level);
                                    for (int i = 0; i < num2; i++)
                                    {
                                        state.ActionStack.Add(new DecreasePopulationAction(
                                            tileData.owner, tileData.rulingCityCoordinates, 200));
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
                            instance.Render();
                    }
                    ReactionUtils.UpdateSurroundingBordersAndTransportPaths(cityTile.owner, tile);
                    InvalidateCityCaches();
                }

                return true;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError(
                    $"[Conquest] Error in DestroyImprovementAction Prefix: {ex}");
                return true;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UnitDataExtensions), nameof(UnitDataExtensions.GetDefenceBonus))]
        private static void GetDefenceBonus_Citadel(
            UnitState unit, GameState gameState, ref int __result)
        {
            TileData tile = gameState.Map.GetTile(unit.coordinates);
            if (tile == null || tile.improvement == null)
                return;

            var citadelType = EnumCache<ImprovementData.Type>.GetType("citadel");

            if (tile.improvement.type == citadelType && tile.owner == unit.owner)
                __result = 15;

            if (tile.unit != null
                && tile.improvement.type == citadelType
                && (UnitDataExtensions.HasAbility(tile.unit, UnitAbility.Type.Hide)
                    || tile.unit.type == UnitData.Type.Dagger
                    || tile.unit.type == UnitData.Type.Giant))
            {
                return;
            }

            if (tile.improvement.type == citadelType
                && tile.terrain == TerrainData.Type.Mountain
                && tile.unit?.UnitData.attack <= 30
                && tile.owner == unit.owner)
            {
                __result = 40;
            }

            if (tile.improvement.type == citadelType
                && tile.terrain.IsWater()
                && unit.UnitData.type == UnitData.Type.Rammership
                && tile.owner == unit.owner)
            {
                __result = 40;
            }
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(CommandUtils), nameof(CommandUtils.GetTrainableUnits))]
        private static void GetTrainableUnits_Citadel(
            GameState gameState,
            PlayerState player,
            TileData tile,
            ref Il2CppSystem.Collections.Generic.List<TrainCommand> __result,
            bool includeUnavailable = false)
        {
            try
            {
                if (gameState?.Map == null || player == null || tile?.improvement == null)
                    return;

                var citadelType = EnumCache<ImprovementData.Type>.GetType("citadel");
                if (tile.improvement.type != citadelType)
                    return;

                if (tile.owner != player.Id)
                    return;

                // Tax Reform: no training from this citadel or its ruling city
                var taxReform = EnumCache<CityReward>.GetType("taxreform");
                if (tile.improvement.HasReward(taxReform))
                {
                    __result = new Il2CppSystem.Collections.Generic.List<TrainCommand>();
                    return;
                }

                if (tile.rulingCityCoordinates != WorldCoordinates.NULL_COORDINATES)
                {
                    TileData cityTile = gameState.Map.GetTile(tile.rulingCityCoordinates);
                    if (cityTile?.improvement != null && cityTile.improvement.HasReward(taxReform))
                    {
                        __result = new Il2CppSystem.Collections.Generic.List<TrainCommand>();
                        return;
                    }
                }

                var list = new Il2CppSystem.Collections.Generic.List<TrainCommand>();

                if (!tile.terrain.IsWater())
                {
                    foreach (UnitData unitData in
                        gameState.GameLogicData.GetUnlockedUnits(player, gameState, false))
                    {
                        if (CommandValidation.HasUnitTerrain(gameState, tile.coordinates, unitData)
                            && unitData.cost < 8)
                        {
                            var trainCommand =
                                new TrainCommand(player.Id, unitData.type, tile.coordinates);
                            if (!player.blockTrainUnits
                                && (includeUnavailable || trainCommand.IsValid(gameState)))
                            {
                                list.Add(trainCommand);
                            }
                        }
                    }
                }
                else
                {
                    foreach (UnitData unitData in
                        gameState.GameLogicData.GetUnlockedUnits(player, gameState, true))
                    {
                        if (CommandValidation.HasUnitTerrain(gameState, tile.coordinates, unitData)
                            && unitData.type == UnitData.Type.Rammership)
                        {
                            var trainCommand =
                                new TrainCommand(player.Id, unitData.type, tile.coordinates);
                            if (!player.blockTrainUnits
                                && (includeUnavailable || trainCommand.IsValid(gameState)))
                            {
                                list.Add(trainCommand);
                            }
                        }
                    }
                }

                __result = list;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Train] GetTrainableUnits_Citadel: {ex}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(TrainCommand), nameof(TrainCommand.IsValid))]
        public static void IsValid_CitadelTrain(
            TrainCommand __instance,
            GameState state,
            ref bool __result,
            out string validationError)
        {
            try
            {
                validationError = "";
                __result = true;
                if (!__instance.PassesBasicValidation(state, out validationError))
                {
                    __result = false;
                }
                PlayerState playerState;
                if (!state.TryGetPlayer(__instance.PlayerId, out playerState))
                {
                    validationError = "Player does not exist";
                    __result = false;
                }
                UnitData unitData;
                if (!state.GameLogicData.TryGetData(__instance.Type, out unitData))
                {
                    validationError = "Missing unit data";
                    __result = false;
                }
                if (!playerState.CanAfford(unitData))
                {
                    validationError = "Not enough resources";
                    __result = false;
                }
                TileData tile = state.Map.GetTile(__instance.Coordinates);
                if (state.Settings.RulesGameMode == EnumCache<GameMode>.GetType("conquest")
                    || state.Settings.RulesGameMode == EnumCache<GameMode>.GetType("reign"))
                {
                    if (tile.improvement != null
                        && tile.terrain.IsWater()
                        && tile.improvement.type
                            == EnumCache<ImprovementData.Type>.GetType("citadel"))
                    {
                        // water citadel train override
                    }
                    else
                    {
                        if (!playerState.CanTrainUnit(state, __instance.Type))
                        {
                            validationError = "Not unlocked";
                            __result = false;
                        }
                    }
                }
                else if (!playerState.CanTrainUnit(state, __instance.Type))
                {
                    validationError = "Not unlocked";
                    __result = false;
                }
                if (!CommandValidation.HasUnitTerrain(state, __instance.Coordinates, unitData))
                {
                    validationError = "Unit can't move to any surrounding tile";
                    __result = false;
                }
                if (CommandValidation.HasUnit(state, __instance.Coordinates))
                {
                    validationError = "Tile is occupied by unit";
                    __result = false;
                }
                if (!unitData.HasAbility(UnitAbility.Type.Independent)
                    && !CommandValidation.CanCitySupportUnit(state, __instance.Coordinates))
                {
                    validationError = "City can't support more units";
                    __result = false;
                }
            }
            catch (Exception ex)
            {
                validationError =
                    $"[Conquest-Train] Error in TrainCommand validation Postfix: {ex}";
                Loader.modLogger?.LogError($"{validationError}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ActionUtils), nameof(ActionUtils.TrainUnit))]
        private static void TrainUnit_FindHome(
            GameState gameState,
            PlayerState playerState,
            TileData tile,
            UnitData unitData,
            UnitState __result)
        {
            try
            {
                if (__result == null || tile == null) return;

                if (tile.improvement != null
                    && tile.improvement.type == EnumCache<ImprovementData.Type>.GetType("citadel"))
                {
                    __result.home = tile.rulingCityCoordinates;
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError(
                    $"[Conquest-Train] Shielded error in TrainUnit Postfix: {ex.Message}");
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ActionUtils), nameof(ActionUtils.GetCityAreaSorted))]
        private static bool GetCityAreaSorted_Conquest(
            GameState gameState,
            TileData cityTile,
            ref Il2CppSystem.Collections.Generic.List<TileData> __result)
        {
            try
            {
                if (gameState?.Map == null || cityTile == null)
                {
                    __result = new Il2CppSystem.Collections.Generic.List<TileData>();
                    return false;
                }

                if (cityTile.improvement == null
                    || cityTile.improvement.type != ImprovementData.Type.City)
                {
                    __result = new Il2CppSystem.Collections.Generic.List<TileData>();
                    return false;
                }

                if (gameState.Settings == null
                    || (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                        && gameState.Settings.RulesGameMode
                            != EnumCache<GameMode>.GetType("reign")))
                {
                    return true;
                }

                if (cityTile.owner == 0 || !gameState.TryGetPlayer(cityTile.owner, out _))
                {
                    __result = new Il2CppSystem.Collections.Generic.List<TileData>();
                    return false;
                }

                WorldCoordinates centerCoords =
                    cityTile.rulingCityCoordinates == WorldCoordinates.NULL_COORDINATES
                        ? cityTile.coordinates
                        : cityTile.rulingCityCoordinates;

                long key = CityKey(centerCoords);
                if (CityAreaCache.TryGetValue(key, out var cached) && cached != null)
                {
                    __result = cached;
                    return false;
                }

                TileData cityCenter = gameState.Map.GetTile(centerCoords);
                if (cityCenter == null)
                {
                    __result = new Il2CppSystem.Collections.Generic.List<TileData>();
                    return false;
                }

                var list = new Il2CppSystem.Collections.Generic.List<TileData>();
                var visited = new HashSet<WorldCoordinates>();
                var queue = new Queue<TileData>();

                queue.Enqueue(cityCenter);
                visited.Add(cityCenter.coordinates);

                while (queue.Count > 0)
                {
                    TileData current = queue.Dequeue();
                    if (current == null) continue;
                    list.Add(current);

                    TileData[] neighbors =
                        MapDataExtensions.GetTileNeighborsSorted(
                            gameState.Map, current.coordinates);
                    if (neighbors == null) continue;

                    for (int i = 0; i < neighbors.Length; i++)
                    {
                        TileData n = neighbors[i];
                        if (n == null || visited.Contains(n.coordinates)) continue;

                        if (n.coordinates == centerCoords
                            || n.rulingCityCoordinates == centerCoords)
                        {
                            visited.Add(n.coordinates);
                            queue.Enqueue(n);
                        }
                    }
                }

                bool looksIncomplete = list.Count <= 1;
                if (!looksIncomplete)
                    CityAreaCache[key] = list;

                __result = list;
                return false;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest] GetCityAreaSorted: {ex}");
                __result = new Il2CppSystem.Collections.Generic.List<TileData>();
                return false;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GameLogicData), nameof(GameLogicData.HasImprovementWithinCityBorders))]
        private static bool HasImprovementWithinCityBorders_Conquest(
            MapData map,
            WorldCoordinates cityCoordinates,
            ImprovementData.Type improvementType,
            ref bool __result)
        {
            try
            {
                if (map == null)
                    return true;

                GameState gameState = GameManager.GameState;
                if (gameState?.Settings == null)
                    return true;

                if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return true;
                }

                TileData cityTile = map.GetTile(cityCoordinates);
                if (cityTile == null)
                {
                    __result = false;
                    return false;
                }

                var area = ActionUtils.GetCityAreaSorted(gameState, cityTile);
                if (area == null)
                {
                    __result = false;
                    return false;
                }

                for (int i = 0; i < area.Count; i++)
                {
                    TileData tile = area[i];
                    if (tile?.improvement != null && tile.improvement.type == improvementType)
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
                Loader.modLogger?.LogError($"[Conquest] HasImprovementWithinCityBorders: {ex}");
                return true;
            }
        }

        static readonly Dictionary<long, Il2CppSystem.Collections.Generic.List<TileData>>
            CityAreaCache = new();

        // count + founded-this-turn, keyed by city, valid for one turn
        static readonly Dictionary<long, (int count, bool builtThisTurn, int turn)>
            CitadelInfoCache = new();

        static long CityKey(WorldCoordinates c) => ((long)c.X << 32) | (uint)c.Y;

        public static void InvalidateCityCaches()
        {
            CityAreaCache.Clear();
            CitadelInfoCache.Clear();
            AI_2.citadelCacheTurn = -1;
        }

        /// <summary>
        /// Single pass (cached per city per turn): total citadels + whether any was founded this turn.
        /// </summary>
        public static int CountCityCitadel(
            GameState gameState,
            TileData tile,
            out bool builtThisTurn)
        {
            builtThisTurn = false;
            if (gameState?.Map == null || tile == null)
                return 0;

            WorldCoordinates center =
                tile.rulingCityCoordinates != WorldCoordinates.NULL_COORDINATES
                    ? tile.rulingCityCoordinates
                    : tile.coordinates;

            long key = CityKey(center);
            int turn = (int)gameState.CurrentTurn;

            if (CitadelInfoCache.TryGetValue(key, out var cached) && cached.turn == turn)
            {
                builtThisTurn = cached.builtThisTurn;
                return cached.count;
            }

            var citadelType = EnumCache<ImprovementData.Type>.GetType("citadel");
            int count = 0;
            bool founded = false;

            for (int i = 0; i < gameState.Map.Tiles.Length; i++)
            {
                TileData t = gameState.Map.Tiles[i];
                if (t?.improvement == null) continue;
                if (t.improvement.type != citadelType) continue;

                WorldCoordinates rule =
                    t.rulingCityCoordinates != WorldCoordinates.NULL_COORDINATES
                        ? t.rulingCityCoordinates
                        : t.coordinates;
                if (rule != center) continue;

                count++;
                if (t.improvement.founded == turn)
                    founded = true;
            }

            CitadelInfoCache[key] = (count, founded, turn);
            builtThisTurn = founded;
            return count;
        }

        public static int CountCityCitadel(GameState gameState, TileData tile)
            => CountCityCitadel(gameState, tile, out _);

        public static bool CityHasMaxCitadel(
            GameState gameState, TileData tile, PlayerState playerState, int citadelCount)
        {
            TileData cityTile =
                GameManager.GameState.Map.GetTile(tile.rulingCityCoordinates);
            int cityLimit = 0;
            int capitalLimit = 0;

            if (gameState.Settings.MapSize <= 11)
            {
                cityLimit = 2;
                capitalLimit = 2;
            }
            else if (gameState.Settings.MapSize <= 16)
            {
                cityLimit = 3;
                capitalLimit = 3;
            }
            else if (gameState.Settings.MapSize <= 20)
            {
                cityLimit = 4;
                capitalLimit = 4;
            }
            else
            {
                cityLimit = 7;
                capitalLimit = 7;
            }

            if (gameState.Settings.mapPreset == MapPreset.Continents
                || gameState.Settings.mapPreset == MapPreset.Pangea)
            {
                cityLimit = cityLimit > 4 ? cityLimit + 1 : cityLimit + 2;
                capitalLimit = capitalLimit > 4 ? capitalLimit + 1 : capitalLimit + 2;
            }

            if (tile.terrain == TerrainData.Type.Mountain
                && !playerState.HasAbility(
                    EnumCache<PlayerAbility.Type>.GetType("mountaincitadel"), gameState))
            {
                return true;
            }
            if ((tile.terrain == TerrainData.Type.Water
                    || tile.terrain == TerrainData.Type.Ocean)
                && !playerState.HasAbility(
                    EnumCache<PlayerAbility.Type>.GetType("watercitadel"), gameState))
            {
                return true;
            }

            if (cityTile.capitalOf != 0 && citadelCount >= capitalLimit)
                return true;
            if (cityTile.capitalOf == 0 && citadelCount >= cityLimit)
                return true;
            return false;
        }

        // =========================================================================
        // F. Citadel Logics (water)
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(PathFinder), nameof(PathFinder.IsTileAccessible))]
        private static void IsTileAccessible_DenyUnusualUnits(
            TileData tile, TileData origin, PathFinderSettings settings, ref bool __result)
        {
            if (origin.unit != null && tile.improvement != null)
            {
                if (UnitDataExtensions.HasAbility(origin.unit, UnitAbility.Type.Hide)
                    || origin.unit.type == UnitData.Type.Dagger
                    || origin.unit.type == UnitData.Type.Giant)
                {
                    if (tile.terrain.IsWater()
                        && tile.improvement.type
                            == EnumCache<ImprovementData.Type>.GetType("citadel"))
                    {
                        __result = false;
                    }
                }
            }
        }

        // =========================================================================
        // G. Citadel Logics (capture)
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(CaptureCommand), nameof(CaptureCommand.IsValid))]
        private static void IsValid_CitadelCapture(
            CaptureCommand __instance,
            GameState state,
            ref bool __result,
            out string validationError)
        {
            try
            {
                validationError = "";
                __result = true;
                if (!__instance.PassesBasicValidation(state, out validationError))
                    __result = false;

                TileData tile = state.Map.GetTile(__instance.Coordinates);
                if (tile == null)
                {
                    validationError = "Missing tile";
                    __result = false;
                }
                if (!tile.HasImprovement(ImprovementData.Type.City)
                    && !tile.HasImprovement(
                        EnumCache<ImprovementData.Type>.GetType("citadel")))
                {
                    validationError = "Missing city or citadel";
                    __result = false;
                }
                UnitState unitState;
                if (!state.TryGetUnit(__instance.UnitId, out unitState))
                {
                    validationError = "Tile is missing unit";
                    __result = false;
                }
                if (!unitState.CanCapture(state, tile, false, true))
                {
                    validationError = "Can't capture";
                    __result = false;
                }
            }
            catch (Exception ex)
            {
                validationError = $"[Conquest-Capture] Error in CaptureCommand validation: {ex}";
                Loader.modLogger?.LogError($"{validationError}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UnitDataExtensions), nameof(UnitDataExtensions.CanCapture))]
        private static void CanCapture_Citadel(
            UnitState unitState,
            GameState gameState,
            TileData tile,
            ref bool __result,
            bool includeNextTurn = false,
            bool allowOnlyOnTile = true)
        {
            try
            {
                PlayerState player;
                __result = unitState.owner != byte.MaxValue
                    && (includeNextTurn || (unitState.CanMove() && unitState.CanAttack()))
                    && (!allowOnlyOnTile || !(tile.coordinates != unitState.coordinates))
                    && !unitState.HasLeader()
                    && (!gameState.TryGetPlayer(unitState.owner, out player)
                        || !player.HasPeaceWith(tile.owner))
                    && tile.improvement != null
                    && tile.owner != unitState.owner
                    && (tile.improvement.type == ImprovementData.Type.City
                        || tile.improvement.type
                            == EnumCache<ImprovementData.Type>.GetType("citadel"));
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Capture] Error in CanCapture: {ex.Message}");
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CaptureCityAction), nameof(CaptureCityAction.ExecuteDefault))]
        private static bool CaptureCityAction_Citadel(
            CaptureCityAction __instance, GameState gameState)
        {
            try
            {
                TileData cityTile = gameState.Map.GetTile(__instance.Coordinates);
                PlayerState? attacker = null;
                gameState.TryGetPlayer(__instance.PlayerId, out attacker);

                if (cityTile != null
                    && cityTile.improvement != null
                    && cityTile.improvement.type
                        == EnumCache<ImprovementData.Type>.GetType("citadel")
                    && attacker != null)
                {
                    gameState.ActionStack.Add(
                        new DestroyImprovementAction(__instance.PlayerId, __instance.Coordinates));
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError(
                    $"[Conquest-Capture] Error in CaptureCommand Postfix: {ex.Message}");
                return true;
            }
        }

        // =========================================================================
        // H. Tech Cost & City Destruction Handler
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameLogicData), nameof(GameLogicData.GetTechPrice))]
        private static void GetTechPrice_Conquest(
            GameLogicData __instance,
            TechData techData,
            PlayerState playerState,
            GameState state,
            ref int __result)
        {
            if (state == null || techData == null) return;
            try
            {
                if (GameManager.GameState.Settings.RulesGameMode
                        != EnumCache<GameMode>.GetType("conquest")
                    && GameManager.GameState.Settings.RulesGameMode
                        != EnumCache<GameMode>.GetType("reign"))
                {
                    return;
                }

                float delayedTurn = Math.Max((float)state.CurrentTurn - 4, 0);
                float adjustedCities = Math.Max((float)playerState.cities * 2, 2);
                float num = Math.Max(
                    4 + techData.cost, playerState.cities + delayedTurn * techData.cost);
                num = (float)Math.Min(num, techData.cost * adjustedCities);

                if (__instance.HasAbility(playerState, PlayerAbility.Type.Literacy))
                    num *= 0.66666f;

                __result = (int)Math.Ceiling((double)num);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Tech] Error: {ex.Message}");
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CaptureCityAction), nameof(CaptureCityAction.ExecuteDefault))]
        private static bool CaptureCityAction_Conquest(
            CaptureCityAction __instance, GameState gameState)
        {
            if (gameState?.Settings == null) return true;
            try
            {
                if (GameManager.GameState.Settings.RulesGameMode
                        != EnumCache<GameMode>.GetType("conquest")
                    && GameManager.GameState.Settings.RulesGameMode
                        != EnumCache<GameMode>.GetType("reign"))
                {
                    return true;
                }

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

        public static void DestroyCityConquest(
            GameState gameState, TileData cityTile, PlayerState playerState, bool isCityUpgrade)
        {
            if (cityTile?.improvement?.type != ImprovementData.Type.City) return;

            InvalidateCityCaches();

            WorldCoordinates center = cityTile.coordinates;
            byte originalOwnerId = cityTile.owner;
            PlayerState originalOwner;
            gameState.TryGetPlayer(originalOwnerId, out originalOwner);

            int transferredPopulation = 0;
            if (originalOwner != null)
            {
                transferredPopulation = cityTile.improvement.population;

                if (originalOwner.cities > 0)
                {
                    originalOwner.cities--;
                    Loader.modLogger?.LogInfo(
                        $"[Conquest] Player {originalOwner.Id} lost a city. Total remaining: {originalOwner.cities}");
                }
            }

            if (transferredPopulation > 0 && originalOwner != null)
            {
                if (!isCityUpgrade)
                {
                    TileData? fleeCityTile = null;
                    int closestDistance = int.MaxValue;

                    for (int i = 0; i < gameState.Map.Tiles.Length; i++)
                    {
                        TileData tile = gameState.Map.Tiles[i];
                        if (!tile.HasImprovement(ImprovementData.Type.City)
                            || tile.owner != originalOwnerId
                            || tile.coordinates == cityTile.coordinates)
                            continue;

                        bool isSieged =
                            tile.unit != null && tile.unit.owner != originalOwnerId;
                        if (isSieged) continue;

                        int distance = MapDataExtensions.ManhattanDistance(
                            cityTile.coordinates, tile.coordinates);
                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                            fleeCityTile = tile;
                        }
                    }

                    if (fleeCityTile != null)
                    {
                        fleeCityTile.improvement.AddPopulation((short)transferredPopulation);
                        Loader.modLogger?.LogInfo(
                            $"[Conquest] Transferred {transferredPopulation} populations from razed city to safe city at {fleeCityTile.coordinates}.");
                    }
                    else
                    {
                        Loader.modLogger?.LogInfo(
                            $"[Conquest] No safe, un-sieged cities found for Player {originalOwnerId}. Population permanently lost.");
                    }
                }
                else
                {
                    TileData capital =
                        GameManager.GameState.Map.GetTile(playerState.startTile);
                    if (capital != null)
                    {
                        for (int j = 0; j < 3; j++)
                        {
                            gameState.ActionStack.Add(new IncreasePopulationAction(
                                playerState.Id, cityTile.coordinates, capital.coordinates, 60));
                        }
                        playerState.currency += 3;
                        Loader.modLogger?.LogInfo(
                            $"[Conquest-Tech] Transferred 3 populations from abandoned city to capital at {capital.coordinates}.");
                    }
                    else
                    {
                        Loader.modLogger?.LogInfo(
                            $"[Conquest-Tech] Capital not owned by Player {originalOwnerId}. Population permanently lost.");
                    }
                }
            }

            int reward = Math.Min(15, cityTile.improvement.level * 2)
                + Math.Min(15, (int)gameState.CurrentTurn);
            int score = 100 + cityTile.improvement.level * 50;
            gameState.ActionStack.Add(
                new IncreaseScoreAction(playerState.Id, score, cityTile.coordinates, 50));

            if (playerState != null && !isCityUpgrade)
            {
                playerState.Currency += reward;
                Loader.modLogger?.LogInfo(
                    $"[Conquest] City destroyed by player {playerState.Id} (+{reward} stars & {score} scores)");
            }

            var citadelType = EnumCache<ImprovementData.Type>.GetType("citadel");
            var toClear = new List<TileData>();

            for (int i = 0; i < gameState.Map.Tiles.Length; i++)
            {
                TileData t = gameState.Map.Tiles[i];
                if (t == null) continue;

                bool isCenter = t.coordinates == center;
                bool ruledByCity = t.rulingCityCoordinates == center;
                bool orphanCitadel =
                    t.improvement != null
                    && t.improvement.type == citadelType
                    && t.owner == originalOwnerId
                    && (t.rulingCityCoordinates == center
                        || t.rulingCityCoordinates == WorldCoordinates.NULL_COORDINATES);

                if (isCenter || ruledByCity || orphanCitadel)
                    toClear.Add(t);
            }

            for (int j = 0; j < toClear.Count; j++)
            {
                TileData territoryTile = toClear[j];
                if (territoryTile == null) continue;

                int num = ScoreSheet.tileValue;
                if (territoryTile.improvement != null
                    && territoryTile.coordinates != cityTile.coordinates)
                {
                    num += gameState.CalculateImprovementScore(territoryTile);
                }

                if (territoryTile.owner != 0)
                    gameState.ActionStack.Add(
                        new DecreaseScoreAction(territoryTile.owner, num));

                if (territoryTile.coordinates != cityTile.coordinates
                    && territoryTile.improvement != null
                    && territoryTile.improvement.type != ImprovementData.Type.LightHouse)
                {
                    territoryTile.improvement = null;
                    if (territoryTile.improvement != null
                        && territoryTile.improvement.type == ImprovementData.Type.Bridge
                        && territoryTile.unit != null
                        && !territoryTile.unit.HasAbility(UnitAbility.Type.Swim)
                        && !territoryTile.unit.HasAbility(UnitAbility.Type.Fly))
                    {
                        gameState.ActionStack.Add(
                            new KillUnitAction(originalOwner.Id, territoryTile.coordinates));
                    }
                }

                territoryTile.owner = 0;
                territoryTile.rulingCityCoordinates = WorldCoordinates.NULL_COORDINATES;

                Tile instance = territoryTile.GetInstance();
                if (instance != null)
                {
                    instance.StopFire();
                    instance.Render();
                }
            }

            if (playerState != null)
                ReactionUtils.UpdateSurroundingBordersAndTransportPaths(playerState.Id, cityTile);

            if (!isCityUpgrade)
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
                cityTile.improvement = null;
            }

            cityTile.owner = 0;
            cityTile.rulingCityCoordinates = WorldCoordinates.NULL_COORDINATES;

            if (playerState != null
                && originalOwner != null
                && cityTile.capitalOf != 0
                && gameState.Settings.RulesGameMode == EnumCache<GameMode>.GetType("reign"))
            {
                Il2CppSystem.Collections.Generic.List<TileData> cityList =
                    originalOwner.GetCityTiles(gameState);
                foreach (TileData targetTile in cityList)
                {
                    if (targetTile != null && targetTile.coordinates != center)
                        DestroyCityConquest(gameState, targetTile, playerState, false);
                }
            }

            if (originalOwner != null
                && playerState != null
                && !originalOwner.IsAlive(
                    gameState, gameState.Settings.rules.PlayerDeathCondition))
            {
                originalOwner.wipedAtCommandIndex = gameState.CommandStack.Count - 1;
                gameState.ActionStack.Add(
                    new WipePlayerAction(playerState.Id, originalOwner.Id));
            }

            InvalidateCityCaches();
            Loader.modLogger?.LogInfo(
                $"[Conquest] City at {cityTile.coordinates} has been successfully razed.");
        }

        // =========================================================================
        // I. Win Conditions
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameState), nameof(GameState.TryGetWinner))]
        private static void TryGetWinner_Conquest(
            GameState __instance, ref bool __result, ref PlayerState winner)
        {
            if (__result) return;
            if (__instance == null || __instance.Settings == null) return;

            try
            {
                var playersSortedByRank = __instance.GetPlayersSortedByRank();
                if (playersSortedByRank == null || playersSortedByRank.Count == 0) return;

                PlayerState topWinner = playersSortedByRank[0];
                if (topWinner == null) return;

                if (__instance.Settings.RulesGameMode == EnumCache<GameMode>.GetType("conquest"))
                {
                    int num = GameStateUtils.CountAlivePlayers(__instance);
                    if (num <= 1)
                    {
                        winner = topWinner;
                        __result = true;
                        return;
                    }
                }

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
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest] Error in TryGetWinner Postfix: {ex}");
            }
        }

        // =========================================================================
        // J. Visuals
        // =========================================================================
        private static Il2CppSystem.Action? _activePopupCallbackHolder;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CaptureCityReaction), nameof(CaptureCityReaction.Execute))]
        public static bool CaptureCityReaction_Conquest(
            CaptureCityReaction __instance, Il2CppSystem.Action onComplete)
        {
            try
            {
                if (GameManager.GameState.Settings.RulesGameMode
                        != EnumCache<GameMode>.GetType("conquest")
                    && GameManager.GameState.Settings.RulesGameMode
                        != EnumCache<GameMode>.GetType("reign"))
                {
                    return true;
                }

                TileData tile =
                    GameManager.GameState.Map.GetTile(__instance.action.Coordinates);
                PlayerState playerState;
                GameManager.GameState.TryGetPlayer(__instance.action.PlayerId, out playerState);
                PlayerState prevOwnerState;
                bool hasPreviousOwner = GameManager.GameState.TryGetPlayer(
                    __instance.action.OldOwnerId, out prevOwnerState);
                bool isPreviousOwnerCapital =
                    hasPreviousOwner && tile.capitalOf == __instance.action.OldOwnerId;
                bool flag = isPreviousOwnerCapital
                    && GameManager.IsPlayerViewing(__instance.action.OldOwnerId)
                    && !GameManager.Client.IsSpectating;
                Tile instance = tile.GetInstance();
                byte attackerId = __instance.action.PlayerId;

                if (instance != null)
                {
                    AudioManager.PlaySFXAtTile(
                        SFXTypes.Capture, tile.coordinates, 0, 1f, 1f);
                    instance.Render();
                    instance.SpawnShine(2f);
                    instance.SpawnSparkles(2f);
                    instance.StopFire();

                    ReactionUtils.UpdateSurroundingBordersAndTransportPaths(attackerId, tile);
                    ResourceManager.AddResourceOfTypeToResourceBar(
                        attackerId, ResourceManager.Type.Score, __instance.action.Score,
                        tile.coordinates, null, "None");

                    _activePopupCallbackHolder = onComplete;
                    ExecutePopupLogic(
                        __instance, _activePopupCallbackHolder, tile, playerState,
                        prevOwnerState, isPreviousOwnerCapital, instance, attackerId);
                }

                Il2CppSystem.Collections.Generic.List<TileData> areaSorted =
                    ActionUtils.GetCityAreaSorted(GameManager.GameState, tile);
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
                    Tile tileInstance =
                        MapRenderer.Current.GetTileInstance(__instance.action.PreviousHomeTown);
                    if (tileInstance != null && !tileInstance.IsHidden)
                        tileInstance.Render();
                }
                if (!GameManager.Client.IsReplay)
                {
                    InputEvents.SelectionCleared();
                    ResourceManager.IncomeChanged(__instance.action.PlayerId);
                }
                if (!flag)
                    GameManager.DelayCall(2500, onComplete);
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
                string? type = "itadel";
                if (tile.improvement.type == ImprovementData.Type.Ruin)
                    type = "ity";

                if (GameManager.IsPlayerViewing((byte)attackerId)
                    && !GameManager.Client.IsSpectating)
                {
                    if (!CameraController.Instance.isTechViewEnabled == true)
                    {
                        CameraController.Instance.CenterOnPosition(
                            tile.coordinates.ToPosition(), 0.8f, null, false);
                    }

                    string tribeName = prevOwnerState.tribe.GetName();
                    string capitalized =
                        char.ToUpper(tribeName[0]) + tribeName.Substring(1);

                    string title = isPreviousOwnerCapital ? "Good News!" : $"C{type} Razed!";
                    string message = isPreviousOwnerCapital
                        ? $"You have razed the {capitalized} capital! All their trade connections are destroyed forever."
                        : $"The c{type} is now a ruin on the ground.";
                    int time = isPreviousOwnerCapital ? 5 : 3;

                    NotificationBase ntf = NotificationManager.GetBasicNotification();
                    ntf.header.text = title;
                    ntf.description.text = message;
                    ntf.showTime = time;
                    ntf.Show();
                }
                else if (GameManager.IsPlayerViewing(__instance.action.OldOwnerId)
                    && !GameManager.Client.IsSpectating)
                {
                    if (!CameraController.Instance.isTechViewEnabled == true)
                    {
                        CameraController.Instance.CenterOnPosition(
                            tile.coordinates.ToPosition(), 0.8f, null, false);
                    }

                    string linkedTribeNameWithSpace =
                        playerState.GetLinkedTribeNameWithSpace(GameManager.GameState);

                    string title = isPreviousOwnerCapital ? "Bad News!" : $"C{type} Razed!";
                    string message = isPreviousOwnerCapital
                        ? $"Your capital has fallen to {linkedTribeNameWithSpace}. All your trade connections are lost forever."
                        : $"Your c{type} is wiped out from existence.";

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

                        PopupBase.PopupButtonData[] array = new PopupBase.PopupButtonData[1];
                        array[0] = new PopupBase.PopupButtonData(
                            "buttons.ok",
                            PopupBase.PopupButtonData.States.Selected,
                            onComplete,
                            -1,
                            true,
                            null);

                        basicPopup.buttonData = array;
                        basicPopup.RefreshButtonState();
                        basicPopup.Show();
                    }
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Popup] ExecutePopupLogic error: {ex}");
                onComplete?.Invoke();
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(
            typeof(ReactionUtils),
            nameof(ReactionUtils.UpdateSurroundingBordersAndTransportPaths))]
        private static bool UpdateSurroundingBorders_Irregular(byte playerId, TileData cityTile)
        {
            try
            {
                if (GameManager.GameState?.Map == null || cityTile == null)
                    return false;

                if (cityTile.improvement == null
                    || cityTile.improvement.type != ImprovementData.Type.City)
                {
                    return false;
                }

                var mode = GameManager.GameState.Settings?.RulesGameMode;
                bool irregular =
                    mode == EnumCache<GameMode>.GetType("conquest")
                    || mode == EnumCache<GameMode>.GetType("reign");
                if (!irregular)
                    return true;

                WorldCoordinates cityCoord = cityTile.coordinates;
                MapRenderContext ctx = MapRenderer.ConstructNewRenderContextUgly();
                var territory = ActionUtils.GetCityAreaSorted(GameManager.GameState, cityTile);

                var refresh = new HashSet<WorldCoordinates>();
                if (territory != null)
                {
                    for (int i = 0; i < territory.Count; i++)
                    {
                        TileData tileData = territory[i];
                        if (tileData == null) continue;
                        refresh.Add(tileData.coordinates);

                        var neighbors =
                            GameManager.GameState.Map.GetTileNeighbors(tileData.coordinates);
                        if (neighbors == null) continue;

                        for (int n = 0; n < neighbors.Count; n++)
                        {
                            TileData nb = neighbors[n];
                            if (nb != null)
                                refresh.Add(nb.coordinates);
                        }
                    }
                }

                refresh.Add(cityCoord);
                var ring = GameManager.GameState.Map.GetArea(cityCoord, 1, true, true);
                if (ring != null)
                {
                    for (int i = 0; i < ring.Count; i++)
                        if (ring[i] != null)
                            refresh.Add(ring[i].coordinates);
                }

                foreach (WorldCoordinates c in refresh)
                {
                    TileData tileData2 = GameManager.GameState.Map.GetTile(c);
                    Tile tile = tileData2.GetInstance();
                    if (tile != null && !tile.IsHidden)
                        tile.Render(ctx);
                }

                return false;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning(
                    $"[Conquest] UpdateSurroundingBordersAndTransportPaths Error: {ex.Message}");
                return false;
            }
        }

        // =========================================================================
        // K. Custom Improvements
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameLogicData), nameof(GameLogicData.CanBuild))]
        private static void CanBuild_Chop(
            GameLogicData __instance,
            GameState gameState,
            TileData tile,
            PlayerState playerState,
            ImprovementData improvement,
            ref bool __result)
        {
            if (tile.improvement != null && improvement.type != ImprovementData.Type.Road)
            {
                __result = false;
                return;
            }

            PlayerState? unitOwner = null;
            if (tile.unit != null)
                gameState.TryGetPlayer(tile.unit.owner, out unitOwner);

            if (tile.unit != null
                && tile.unit.owner != playerState.Id
                && !unitOwner.HasPeaceWith(playerState.Id))
            {
                __result = false;
                return;
            }

            if (improvement.HasAbility(ImprovementAbility.Type.Limited)
                && __instance.HasImprovementWithinCityBorders(
                    gameState.Map, tile.rulingCityCoordinates, improvement.type))
            {
                __result = false;
                return;
            }

            try
            {
                if (improvement.HasAbility(ImprovementAbility.Type.Freelance)
                    && improvement.HasAbility(
                        EnumCache<ImprovementAbility.Type>.GetType("freemanual")))
                {
                    if (tile.owner != playerState.Id
                        && (tile.unit == null
                            || tile.unit.owner != playerState.Id
                            || !tile.unit.CanMove()
                            || !tile.unit.CanAttack()))
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
    }
}