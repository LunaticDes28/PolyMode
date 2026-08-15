using HarmonyLib;
using PolytopiaBackendBase.Game;
using PolytopiaBackendBase.Common;
using Polytopia.Data;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine.EventSystems;

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
                if (GameManager.PreliminaryGameSettings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && GameManager.PreliminaryGameSettings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return true;
                }

                if (playerCount > 8)
                {
                    Loader.modLogger?.LogWarning($"[CapitalGenerator] players={playerCount} > 8 → vanilla");
                    return true;
                }

                int mapType = (int)GameManager.PreliminaryGameSettings.mapPreset;
                Loader.modLogger?.LogInfo($"[CapitalGenerator] mapType={mapType} players={playerCount}");

                // Continents (3): uses vanilla continent capitals
                if (mapType == 3)
                {
                    Loader.modLogger?.LogInfo("[CapitalGenerator] Executing vanilla logics...");
                    return true;
                }

                // 1-4 players: uses 2x2 (1-4 players) domains; all domains used
                // 4-8 players: uses 4x4 (4-8 players) domains; non-corner outer-ring domains only
                Loader.modLogger?.LogInfo(
                    $"[CapitalGenerator] Quadrants players={playerCount} width={width}");

                int grid = (playerCount <= 4) ? 2 : 4;
                int domainSize = width / grid;
                if (domainSize < 3)
                {
                    Loader.modLogger?.LogError($"[CapitalGenerator] domainSize={domainSize} too small → vanilla");
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
                        bool isCorner = (domainX == 0 || domainX == 3) && (domainY == 0 || domainY == 3);
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

                    int bestDomain = PickBestDomain(__instance.random, availableDomains, chosenDomains, grid);
                    availableDomains.Remove(bestDomain);
                    chosenDomains.Add(bestDomain);

                    WorldCoordinates domainCoord = WorldCoordinates.FromIndex(bestDomain, grid);
                    int offsetX = Math.Min(remainder, Math.Max(1, Math.Min(remainder, domainCoord.X) - 1));
                    int offsetY = Math.Min(remainder, Math.Max(1, Math.Min(remainder, domainCoord.Y) - 1));
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
                        $"[CapitalGenerator] P{p+1} domain={bestDomain} tile={WorldCoordinates.FromIndex(tileIndex, width)}");
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
                if (GameManager.PreliminaryGameSettings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && GameManager.PreliminaryGameSettings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return true;
                }

                // 1. 檢查地圖類型是否為 Pangea (MapType == 6)
                int mapType = -1;
                try
                {
                    mapType = (int)GameManager.PreliminaryGameSettings.mapPreset;
                }
                catch { }

                if (mapType != 6) return true;

                if (map == null || player == null || targetContinent == null || capitals == null) return true;

                // 2. 模擬原版邏輯：先拿到一個原版偏好的基準點座標
                WorldCoordinates preferredCoords = __instance.GetBestCityCoordinates(gameState, map, targetContinent.Tiles, capitals);
                
                // 應急處理：萬一原版找不到，從該大陸隨機挑一個，否則交給原版處理
                if (preferredCoords == WorldCoordinates.NULL_COORDINATES)
                {
                    if (targetContinent.Tiles != null && targetContinent.Tiles.Count > 0)
                        preferredCoords = targetContinent.Tiles[0]; // 拿大陸的第一個格子做基準
                    else
                        return true; // 讓原版去噴 Warning 或走應急流程
                }

                TileData preferredTile = map.GetTile(preferredCoords);

                // 3. 尋找最適合的沿海格子（傳入 IL2CPP 的 capitals 清單）
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

                // 4. 執行與原版完全相同的安放與註冊邏輯
                preferredTile.owner = player.Id;
                capitals.Add(preferredTile); // 直接寫入 IL2CPP 的 List，洗牌邏輯會完美同步！
                __result = true; 

                // 5. 返回 false 成功攔截，不再執行原版方法
                return false; 
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[CapitalGenerator] TryAddCapitalToContinent Error: {ex}");
                return true; // 發生任何例外時走原版安全機制，防止遊戲卡死
            }
        }

        // 修改後的沿海搜尋演算法：完美支援 IL2CPP List
        private static TileData? FindPangeaCoastTile(
            MapData map, 
            TileData preferred, 
            Il2CppSystem.Collections.Generic.List<TileData> currentCapitals)
        {
            List<TileData> coast = new List<TileData>();

            // 搜集全地圖合法的陸地沿海格子
            for (int i = 0; i < map.Tiles.Length; i++)
            {
                TileData tile = map.Tiles[i];
                if (tile == null || tile.IsWater) continue;
                if (tile.improvement != null) continue;

                bool nearWater = false;
                var neighbors = map.GetTileNeighbors(tile.coordinates);
                if (neighbors == null) continue;

                // 判斷四周是否有水
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
                TileData t = coast[i];
                int minDist = int.MaxValue;

                // 如果目前地圖上還沒有放置任何首都（洗牌或初次安放的第一個玩家）
                if (currentCapitals.Count == 0)
                {
                    minDist = MapDataExtensions.ChebyshevDistance(t.coordinates, preferred.coordinates);
                    int score2 = 1000 - minDist; // 越接近原版偏好點分數越高
                    if (score2 > bestScore)
                    {
                        bestScore = score2;
                        best = t;
                    }
                    continue;
                }

                // 遍歷 IL2CPP List 計算與其它已有首都的 Chebyshev 距離
                for (int a = 0; a < currentCapitals.Count; a++)
                {
                    int d = MapDataExtensions.ChebyshevDistance(t.coordinates, currentCapitals[a].coordinates);
                    if (d < minDist) minDist = d;
                }

                // 核心評分公式：極力拉開與其他首都的距離（minDist * 10），並帶有靠近原版偏好點的微小權重（-bias）
                int bias = MapDataExtensions.ChebyshevDistance(t.coordinates, preferred.coordinates);
                int score = (minDist * 10) - bias;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = t;
                }
            }

            return best;
        }

        /*private static bool GeneratePangeaCapitals(
            MapGenerator gen,
            int width,
            int playerCount,
            ref Il2CppSystem.Collections.Generic.List<int> __result)
        {
            try
            {
                if (playerCount > 8)
                {
                    Loader.modLogger?.LogWarning("[CapitalGenerator] Pangea players>8 → vanilla");
                    return true;
                }

                int grid = (playerCount <= 4) ? 2 : 5;
                int domainSize = width / grid;
                if (domainSize < 3)
                {
                    Loader.modLogger?.LogError("[CapitalGenerator] Pangea domain too small → vanilla");
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
                    availableDomains.AddRange(new[] { 6, 7, 8, 11, 13, 16, 17, 18 });
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
                        gen.AddDistanceToProbabilityTable(
                            probabilities, width, new WorldCoordinates(px - 1, py - 1), domainSize);
                    }
                }

                List<int> chosenDomains = new List<int>();
                List<int> capitalTiles = new List<int>();

                for (int p = 0; p < playerCount; p++)
                {
                    if (availableDomains.Count == 0)
                        break;

                    int domain = PickBestDomain(gen.random, availableDomains, chosenDomains, grid);
                    availableDomains.Remove(domain);
                    chosenDomains.Add(domain);

                    WorldCoordinates domainCoord = WorldCoordinates.FromIndex(domain, grid);
                    int offsetX = Math.Min(remainder, Math.Max(1, Math.Min(remainder, domainCoord.X) - 1));
                    int offsetY = Math.Min(remainder, Math.Max(1, Math.Min(remainder, domainCoord.Y) - 1));
                    int originX = domainCoord.X * domainSize + offsetX;
                    int originY = domainCoord.Y * domainSize + offsetY;

                    int margin = domainSize == 3 ? 1 : 2;
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

                    int maxProb = gen.CalculateProbabilityInRange(
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
                        int roll = gen.random.Range(0, maxProb);
                        tileIndex = gen.IndexForProbabilityValueInRange(
                            probabilities, width, roll, startX, endX, startY, endY);
                    }

                    capitalTiles.Add(tileIndex);
                    Loader.modLogger?.LogInfo(
                        $"[CapitalGenerator] mapType = 3 (Pangea) P{p+1} grid={grid} domain={domain} " +
                        $"tile={WorldCoordinates.FromIndex(tileIndex, width)}");
                }

                __result = new Il2CppSystem.Collections.Generic.List<int>();
                foreach (int idx in capitalTiles)
                    __result.Add(idx);

                return false;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[CapitalGenerator] Pangea: {ex}");
                return true;
            }
        }*/

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

            // --- Try emergency city generation to equalize city distribution---
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
                        Loader.modLogger?.LogInfo("[Conquest-Map] Emergency placement failed: attempt terminated");
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

            // --- Convert excess cities for distrbution into ruins ---
            // Scored by distance weighting methods below
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

                    TileData? picked = FindBestVillageForPlayer(gameState, neutralVillages, kept, player, owned);
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
                foreach (TileData neighbour in gameState.Map.GetTileNeighborsSorted(village.coordinates))
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
                {
                    village.terrain = TerrainData.Type.Mountain;
                }
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
                if (village.improvement == null || village.improvement.type != ImprovementData.Type.City) continue;

                int distCapital = MapDataExtensions.ChebyshevDistance(village.coordinates, capital);
                int distCentroid = MapDataExtensions.ChebyshevDistance(village.coordinates, centroid);

                // Soft Voronoi: prefer tiles closer to mine than to others
                float voronoiPenalty = 0;
                for (int p = 0; p < gameState.PlayerStates.Count; p++)
                {
                    PlayerState other = gameState.PlayerStates[p];
                    if (other == null || other.Id == 255 || other.Id == player.Id) continue;
                    int distOther = MapDataExtensions.ChebyshevDistance(village.coordinates, other.startTile);
                    if (distOther < distCapital)
                    {
                        voronoiPenalty += (float)((distCapital - distOther) * 25);
                    }
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
                            foreach (TileData? tile2 in gameState.Map.GetTileNeighborsSorted(tile.coordinates))
                            {
                                if (tile2.improvement != null && tile2.improvement.type == ImprovementData.Type.City)
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
                {
                    neutralVillages.Add(tile);
                }
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
                    TileData? village = FindBestVillageForPlayer(gameState, neutralVillages, assigned, player, owned);
                    if (village == null) continue;
                    assigned.Add(village.coordinates);
                    owned.Add(village.coordinates);
                    ConquestInitializeCity(gameState, village, player);
                }
            }

            foreach (TileData tile2 in gameState.Map.tiles)
            {
                if (tile2.improvement != null && tile2.improvement.type == ImprovementData.Type.City && tile2.owner == 0)
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
                Loader.modLogger?.LogInfo($"[Conquest-Match] City for P{player.Id} at {tile.coordinates}");
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
                    if (improvement.type == EnumCache<ImprovementData.Type>.GetType("citadel"))
                    {
                        __result = false;
                        return;
                    }
                }

                if (improvement.type == EnumCache<ImprovementData.Type>.GetType("citadel") && tile.owner == playerState.Id)
                {
                    int citadelCount = CountCityCitadel(gameState, tile);
                    __result = !CityHasMaxCitadel(gameState, tile, playerState, citadelCount);
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest] Error in CanBuild Postfix: {ex}");
            }            
        }   

        /*[HarmonyPrefix]
        [HarmonyPatch(typeof(BuildAction), nameof(BuildAction.ExecuteDefault))]
        private static bool BuildAction__DynamicCost(BuildAction __instance, GameState gameState)
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
                        playerState.Currency -= improvementData.GetCurrencyCost() + num * 10;
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
            if (tile == null || tile.improvement == null)
            {
                return;
            }

            if (tile != null && tile?.improvement?.type == EnumCache<ImprovementData.Type>.GetType("citadel") && tile.owner == unit.owner)
            {
                __result = 15;
            }

            if (tile != null && tile.unit != null && tile?.improvement?.type == EnumCache<ImprovementData.Type>.GetType("citadel") && (UnitDataExtensions.HasAbility(tile.unit, UnitAbility.Type.Hide) || tile.unit.type == UnitData.Type.Dagger || tile.unit.type == UnitData.Type.Giant))
            {
                return;
            }

            if (tile != null && tile?.improvement?.type == EnumCache<ImprovementData.Type>.GetType("citadel") && tile.terrain == TerrainData.Type.Mountain && tile?.unit?.UnitData.attack <= 30 && tile.owner == unit.owner)
            {
                __result = 40;
            }

            if (tile != null && tile?.improvement?.type == EnumCache<ImprovementData.Type>.GetType("citadel") && tile.terrain.IsWater() && tile?.unit?.UnitData.attack <= 30 && tile.owner == unit.owner)
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
        [HarmonyPriority(Priority.Last)]
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

                    if (tile.terrain == TerrainData.Type.Water || tile.terrain == TerrainData.Type.Ocean)
                    {
                        return;
                    }

                    foreach (UnitData unitData in gameState.GameLogicData.GetUnlockedUnits(player, gameState, false))
                    {
                        if (CommandValidation.HasUnitTerrain(gameState, tile.coordinates, unitData) && unitData.cost < 8)
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
        private static bool GetCityAreaSorted_Conquest(
            GameState gameState,
            TileData cityTile,
            ref Il2CppSystem.Collections.Generic.List<TileData> __result)
        {
            try
            {
                if (gameState?.Settings == null || gameState.Map == null || cityTile == null)
                {
                    return true;
                }

                if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {    
                    return true;
                }

                if (cityTile.owner == 0)
                {
                    return true;
                }

                if (!gameState.TryGetPlayer(cityTile.owner, out _))
                {
                    return true;
                }

                WorldCoordinates centerCoords =
                    cityTile.rulingCityCoordinates == WorldCoordinates.NULL_COORDINATES
                        ? cityTile.coordinates
                        : cityTile.rulingCityCoordinates;

                // Use the same gameState that was passed in — NOT GameManager.GameState
                TileData cityCenter = gameState.Map.GetTile(centerCoords);
                if (cityCenter == null)
                    return true;

                var list = new Il2CppSystem.Collections.Generic.List<TileData>();
                var visited = new HashSet<WorldCoordinates>();
                var queue = new Queue<TileData>();

                queue.Enqueue(cityCenter);
                visited.Add(cityCenter.coordinates);

                while (queue.Count > 0)
                {
                    TileData current = queue.Dequeue();
                    if (current == null)
                        continue;

                    list.Add(current);

                    TileData[] neighbors = MapDataExtensions.GetTileNeighborsSorted(
                        gameState.Map, current.coordinates);
                    if (neighbors == null) continue;

                    for (int i = 0; i < neighbors.Length; i++)
                    {
                        TileData n = neighbors[i];
                        if (n == null || visited.Contains(n.coordinates))
                            continue;

                        if (n.coordinates == centerCoords
                            || n.rulingCityCoordinates == centerCoords)
                        {
                            visited.Add(n.coordinates);
                            queue.Enqueue(n);
                        }
                    }
                }

                __result = list;
                return false;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest] GetCityAreaSorted: {ex}");
                return true;
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
                    TileData t = area[i];
                    if (t?.improvement != null && t.improvement.type == improvementType)
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

        public static bool CityHasMaxCitadel(GameState gameState, TileData tile, PlayerState playerState, int citadelCount)
        {
            TileData cityTile = GameManager.GameState.Map.GetTile(tile.rulingCityCoordinates);
            int cityLimit = 0;
            int capitalLimit = 0;
            
            if (gameState.Settings.MapSize  <= 11)
            {
                cityLimit = 1;
                capitalLimit = 1;
            }
            else
            if (gameState.Settings.MapSize  <= 16)
            {
                cityLimit = 2;
                capitalLimit = 2;
            }
            else
            if (gameState.Settings.MapSize  <= 20)
            {
                cityLimit = 3;
                capitalLimit = 3;
            }
            else
            {
                cityLimit = 6;
                capitalLimit = 6;
            }

            if (gameState.Settings.mapPreset == MapPreset.Continents || gameState.Settings.mapPreset == MapPreset.Pangea)
            {
                cityLimit = cityLimit > 3? cityLimit + 1 : cityLimit + 2;
                capitalLimit = capitalLimit > 3? capitalLimit + 1 : capitalLimit + 2;
            }

            if (tile.terrain == TerrainData.Type.Mountain && !playerState.HasAbility(EnumCache<PlayerAbility.Type>.GetType("mountaincitadel"), gameState))
            {
                return true;
            }
            if ((tile.terrain == TerrainData.Type.Water || tile.terrain == TerrainData.Type.Ocean) && !playerState.HasAbility(EnumCache<PlayerAbility.Type>.GetType("watercitadel"), gameState))
            {
                return true;
            }

            if (cityTile.capitalOf != 0 && citadelCount >= capitalLimit)
            {
                return true;
            }
            if (cityTile.capitalOf == 0 && citadelCount >= cityLimit)
            {
                return true;
            }
            return false;
        }

        // =========================================================================
        // F. Citadel Logics (water)
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(PathFinder), nameof(PathFinder.IsTileAccessible))]
        private static void IsTileAccessible_Deny(TileData tile, TileData origin, PathFinderSettings settings, ref bool __result)
        {
            if (origin.unit != null && tile.improvement != null)
            {

                if (UnitDataExtensions.HasAbility(origin.unit, UnitAbility.Type.Hide) || origin.unit.type == UnitData.Type.Dagger || origin.unit.type == UnitData.Type.Giant)
                {
                    if (tile.terrain.IsWater() && tile.improvement.type == EnumCache<ImprovementData.Type>.GetType("citadel"))
                    {
                        __result = false;
                    }
                }
            }
        }
        
        /*[HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(EmbarkAction), nameof(EmbarkAction.ExecuteDefault))]
        private static bool ExecuteDefault_WaterCitadel(EmbarkAction __instance, GameState gameState)
        {
            try
            {
                Loader.modLogger?.LogInfo("[Conquest-Citadel] Embarking on rammer 1...");

                PlayerState playerState;
                if (gameState.TryGetPlayer(__instance.PlayerId, out playerState))
                {
                    TileData tile = gameState.Map.GetTile(__instance.Coordinates);
                    UnitState unit = tile.unit;
                    UnitData.Type type = UnitData.Type.Rammership;

                    if (tile.improvement.type != EnumCache<ImprovementData.Type>.GetType("citadel")) return true;
                    Loader.modLogger?.LogInfo("[Conquest-Citadel] Embarking on rammer 2...");

                    UnitData unitData;
                    gameState.GameLogicData.TryGetData(type, out unitData);
                    UnitState unitState = ActionUtils.TrainUnit(gameState, playerState, tile, unitData);
                    if (!UnitDataExtensions.HasAbility(unitState, UnitAbility.Type.Protect))
                    {
                        unitState.health = unit.health;
                    }
                    unitState.home = unit.home;
                    unitState.direction = unit.direction;
                    unitState.flipped = unit.flipped;
                    unitState.passengerUnit = unit;
                    unitState.effects = unit.effects;
                    unitState.attacked = true;
                    unitState.moved = true;

                    gameState.ActionStack.Add(new UpgradeAction(__instance.PlayerId, type, tile.coordinates, 0));
                }
                return false;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest] Error in EmbarkAction Postfix: {ex}");
                return true;
            }            
        }*/

        /*[HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(ActionUtils), nameof(ActionUtils.TrainUnit))]
        private static bool TrainUnit_BypassPolyMod(ref UnitState __result, GameState gameState, PlayerState playerState, TileData tile, ref UnitData unitData)
        {
            if (tile == null || tile.unit == null)
            {
                return true;
            }

            if (unitData.type == UnitData.Type.Transportship && tile.terrain.IsWater() && tile.improvement.type == EnumCache<ImprovementData.Type>.GetType("citadel"))
            {
                gameState.GameLogicData.TryGetData(UnitData.Type.Rammership, out unitData);
                return false;
            }
            return true;
        }*/

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MoveAction), nameof(MoveAction.ExecuteDefault))]
        private static bool MoveAction_WaterCitadel(MoveAction __instance, GameState gameState)
        {
			WorldCoordinates worldCoordinates = __instance.Path[0];
			WorldCoordinates worldCoordinates2 = __instance.Path[__instance.Path.Count - 1];
			TileData tile = gameState.Map.GetTile(worldCoordinates);
			TileData tile2 = gameState.Map.GetTile(worldCoordinates2);

            if (tile == null) return true;

            if (tile.improvement == null) return true;

            if (!tile.terrain.IsWater()) return true;

            if (tile.improvement.type != EnumCache<ImprovementData.Type>.GetType("citadel")) return true;

            PlayerState playerState;
            if (!gameState.TryGetPlayer(__instance.PlayerId, out playerState) || playerState == null)
            {
                return true;
            }

            UnitData unitData;
            if (!gameState.GameLogicData.TryGetData(EnumCache<UnitData.Type>.GetType("citadelrammership"), out unitData) || unitData == null)
            {
                return true;
            }

            if ((tile2.unit.HasAbility(UnitAbility.Type.Water) || tile2.unit.HasAbility(UnitAbility.Type.Swim) || tile2.unit.HasAbility(UnitAbility.Type.Fly)) && tile.terrain.IsWater() && tile.improvement.type == EnumCache<ImprovementData.Type>.GetType("citadel"))
            {
                return true;
            }

            if (tile.terrain.IsWater() && tile.improvement.type == EnumCache<ImprovementData.Type>.GetType("citadel"))
            {
                /*if (tile.unit.passengerUnit != null)
                {
				    gameState.ActionStack.Add(new DisembarkAction(__instance.PlayerId, worldCoordinates));
                }*/

                gameState.TryGetPlayer(__instance.PlayerId, out playerState);    
                UnitState cache = tile2.unit;
                UnitState unitState = ActionUtils.TrainUnit(gameState, playerState, tile, unitData);
			
                unitState.UnitData = unitData;
                unitState.health = (ushort)unitData.health;
                unitState.passengerUnit = null;
                unitState.xp = cache.xp;
                unitState.direction = cache.direction;
                unitState.flipped = cache.flipped;
                unitState.attacked = true;
                unitState.moved = true;

                tile.SetUnit(unitState);
                tile2.SetUnit(null);
                unitState.coordinates = worldCoordinates;

                Tile instance = tile.GetInstance();
                Tile instance2 = tile2.GetInstance();
                instance.Render();
                instance2.Render();

                return false;
            }
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UnitDataExtensions), nameof(UnitDataExtensions.GetAllowedTerrain))]
        private static void GetAllowedTerrain_CitadelRammership(UnitState unit, GameState state, ref Il2CppSystem.Collections.Generic.List<TerrainData>? __result)
        {
            if (unit.type == EnumCache<UnitData.Type>.GetType("citadelrammership"))
            {
                Il2CppSystem.Collections.Generic.List<TerrainData> list = new Il2CppSystem.Collections.Generic.List<TerrainData>();
                foreach (Il2CppSystem.Collections.Generic.KeyValuePair<TerrainData.Type, TerrainData> keyValuePair in state.GameLogicData.AllTerrainData)
				{
					if (keyValuePair.Key == TerrainData.Type.Water || keyValuePair.Key == TerrainData.Type.Ocean)
					{
						list?.Add(keyValuePair.Value);
					}
				}
                __result = list;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(BattleHelpers), nameof(BattleHelpers.GetBattleResults))]
        private static void GetBattleResults_DenyMoveIntoCity(GameState gameState, UnitState attackingUnit, UnitState defendingUnit, ref BattleResults __result)
        {
            TileData cityTile = gameState.Map.GetTile(defendingUnit.coordinates);

            if (attackingUnit.type == EnumCache<UnitData.Type>.GetType("citadelrammership") && cityTile.improvement != null && cityTile.improvement.type == ImprovementData.Type.City)
            {
                __result.shouldMoveToDefeatedEnemyTile = false;
            }
        }

        // =========================================================================
        // G. Tech Cost & City Destruction Handler
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

                float delayedTurn = Math.Max((float)state.CurrentTurn - 3, 0);
                float num = Math.Max(4 + techData.cost, playerState.cities + delayedTurn * techData.cost);
                num = (float)Math.Min(num, techData.cost * (playerState.cities + 0) * 2);
                
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
                        for (int j = 0; j < 3; j++)
                        {
                            gameState.ActionStack.Add(new IncreasePopulationAction(playerState.Id, cityTile.coordinates, capital.coordinates, 60));
                            //instance.AddSubAction(new IncreasePopulationAction(playerState.Id, cityTile.coordinates, capital.coordinates, 60));
                        }
                        playerState.currency += 3;
                        Loader.modLogger?.LogInfo($"[Conquest-Tech] Transferred 3 populations from abandoned city to capital at {capital.coordinates}.");

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
        // H. Win Conditions
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
        // I. Reactions
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
                    if (!CameraController.Instance.isTechViewEnabled == true)
                    {
                        CameraController.Instance.CenterOnPosition(tile.coordinates.ToPosition(), 0.8f, null, false);
                    }

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
                    if (!CameraController.Instance.isTechViewEnabled == true)
                    {
                        CameraController.Instance.CenterOnPosition(tile.coordinates.ToPosition(), 0.8f, null, false);
                    }

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
                        /*basicPopup.buttonData = new PopupBase.PopupButtonData[]
                        {
                            new PopupBase.PopupButtonData("buttons.ok", PopupBase.PopupButtonData.States.Selected, onComplete, -1, true, null)
                        };*/
                        
                        PopupBase.PopupButtonData[] array = new PopupBase.PopupButtonData[1];
						int num = 0;
                        /*UIButtonBase.ButtonAction callback;
                        void OnOkClicked(int id, BaseEventData eventData)
                        {
                            onComplete?.Invoke();
                        }
                        callback = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UIButtonBase.ButtonAction>(OnOkClicked);*/
            
                        array[num] = new PopupBase.PopupButtonData("buttons.ok", PopupBase.PopupButtonData.States.Selected, onComplete, -1, true, null);

                        basicPopup.buttonData = array;
                        Loader.modLogger?.LogInfo($"ButtonData is {basicPopup.buttonData}");
                        basicPopup.RefreshButtonState();
                        Loader.modLogger?.LogInfo("[Conquest-Backend] Button refreshed!");
                        basicPopup.Show();
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