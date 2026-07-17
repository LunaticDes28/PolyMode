using Polytopia.Data;

public enum Faction
{
    Enemy, 
    Owned, 
    Both   
}

public class CityAnalysisResult
{
    public TileData? TargetTile { get; set; }     
    public int EnemyCityCount { get; set; }      
    public int OwnedCityCount { get; set; }      
    public string? TileTypeLabel { get; set; }     
}

namespace PolyMode
{
    public static class MapAnalysisUtils
    {
        /// <summary>
        /// Scans a city's center or its borders to find the best strategic tile based on nearby city counts.
        /// If searchFromCenter is true, optional filtering parameters (findType, findMost) can be safely ignored.
        /// </summary>
        /// <param name="map">The current game map instance.</param>
        /// <param name="gameState">The current global state of the game session.</param>
        /// <param name="cityTile">The core center tile of the target city.</param>
        /// <param name="searchRadius">The radius in tiles to look outward when scanning the area.</param>
        /// <param name="searchFromCenter">Set to true to scan only from the city center (ignores final two optional params); false to scan from the 4 extreme corners of the territory individually.</param>
        /// <param name="currentOwner">The player state performing the analysis, used to correctly evaluate friendly vs hostile relationships.</param>
        /// <param name="findType">Optional. Specifies whether to filter by Enemy, Owned, or Both faction city densities. Defaults to Faction.Both.</param>
        /// <param name="findMost">Optional. Set to true to find the tile with the "highest" density; false to find the one with the "lowest" density. Defaults to true.</param>
        /// <returns>A CityAnalysisResult containing the chosen tile and relevant scanning metrics.</returns>
        public static CityAnalysisResult? ScanCity(
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

                if (topLeft != null) startingPoints["TopLeft"] = topLeft;
                if (topRight != null) startingPoints["TopRight"] = topRight;
                if (bottomLeft != null) startingPoints["BottomLeft"] = bottomLeft;
                if (bottomRight != null) startingPoints["BottomRight"] = bottomRight;
            }

            CityAnalysisResult? bestResult = null;

            foreach (var kvp in startingPoints)
            {
                string label = kvp.Key;
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

        public static void LogAnalysisResult(TileData cityTile, CityAnalysisResult? result, int radius, Faction type = Faction.Both, bool findMost = true)
        {
            if (result == null)
            {
                Loader.modLogger?.LogWarning($"[MapAnalysis] Scan failed or returned no results for City Tile at ({cityTile?.coordinates.X}, {cityTile?.coordinates.Y})");
                return;
            }

            string modeStr = findMost ? "Highest Density" : "Lowest Density";
            int totalDensity = result.EnemyCityCount + result.OwnedCityCount;

            string logMessage = 
                $"\n================== [AI City Analysis] ==================\n" +
                $" * Source City Center : Coordinates=({cityTile.coordinates.X}, {cityTile.coordinates.Y}) | Level={cityTile.improvement.level}\n" +
                $" * Scan Configuration : Radius={radius} | TargetFaction={type} | Selection={modeStr}\n" +
                $" ---------------------------------------------------------------\n" +
                $" * Chosen Best Tile   : {result.TileTypeLabel} at ({result.TargetTile?.coordinates.X}, {result.TargetTile?.coordinates.Y})\n" +
                $" * Nearby Enemy Cities: {result.EnemyCityCount}\n" +
                $" * Nearby Owned Cities: {result.OwnedCityCount}\n" +
                $" * Total City Density : {totalDensity}\n" +
                $"================================================================";

            Loader.modLogger?.LogInfo(logMessage);
        }
    }
}