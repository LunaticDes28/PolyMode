using Polytopia.Data;
using PolytopiaBackendBase.Game;
using System.Collections.Generic;

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
    public static class MapAnalysis
    {
        /// <summary>
        /// Scan from city center only.
        /// </summary>
        public static CityAnalysisResult? ScanCityFromCenter(
            MapData map,
            GameState gameState,
            TileData cityTile,
            int searchRadius,
            PlayerState currentOwner)
        {
            if (gameState == null || cityTile == null || map == null || currentOwner == null)
                return null;

            return EvaluatePoint(
                map, cityTile, cityTile, searchRadius, currentOwner, "CityCenter");
        }

        /// <summary>
        /// Scan the 4 extreme corners of the city territory.
        /// When requireEmptyTile is true, corners that already have an improvement are skipped.
        /// </summary>
        public static CityAnalysisResult? ScanCityForCorners(
            MapData map,
            GameState gameState,
            TileData cityTile,
            int searchRadius,
            PlayerState currentOwner,
            Faction findType = Faction.Both,
            bool findMost = true,
            bool requireEmptyTile = true)
        {
            if (gameState == null || cityTile == null || map == null || currentOwner == null)
                return null;

            var territoryTiles = ActionUtils.GetCityAreaSorted(gameState, cityTile);
            if (territoryTiles == null || territoryTiles.Count == 0)
                return null;

            TileData? topLeft = null;
            TileData? topRight = null;
            TileData? bottomLeft = null;
            TileData? bottomRight = null;

            float maxTopRight = float.MinValue;
            float minBottomLeft = float.MaxValue;
            float maxBottomRight = float.MinValue;
            float minTopLeft = float.MaxValue;

            foreach (var tile in territoryTiles)
            {
                if (tile == null) continue;

                int x = tile.coordinates.X;
                int y = tile.coordinates.Y;
                float sum = x + y;
                float diff = x - y;

                if (sum > maxTopRight) { maxTopRight = sum; topRight = tile; }
                if (sum < minBottomLeft) { minBottomLeft = sum; bottomLeft = tile; }
                if (diff > maxBottomRight) { maxBottomRight = diff; bottomRight = tile; }
                if (diff < minTopLeft) { minTopLeft = diff; topLeft = tile; }
            }

            var corners = new Dictionary<string, TileData>();
            TryAddCorner(corners, "TopLeft", topLeft, map, requireEmptyTile);
            TryAddCorner(corners, "TopRight", topRight, map, requireEmptyTile);
            TryAddCorner(corners, "BottomLeft", bottomLeft, map, requireEmptyTile);
            TryAddCorner(corners, "BottomRight", bottomRight, map, requireEmptyTile);

            CityAnalysisResult? bestResult = null;

            foreach (var pair in corners)
            {
                var currentResult = EvaluatePoint(
                    map, cityTile, pair.Value, searchRadius, currentOwner, pair.Key);
                if (currentResult == null) continue;

                if (bestResult == null)
                {
                    bestResult = currentResult;
                    continue;
                }

                int currentCount = CountByFaction(currentResult, findType);
                int bestCount = CountByFaction(bestResult, findType);

                if (findMost ? currentCount > bestCount : currentCount < bestCount)
                    bestResult = currentResult;
            }

            return bestResult;
        }

        private static void TryAddCorner(
            Dictionary<string, TileData> corners,
            string label,
            TileData? tile,
            MapData map,
            bool requireEmptyTile)
        {
            if (tile == null) return;
            if (requireEmptyTile && tile.improvement != null) return;
            if (MapDataExtensions.DistanceToEdge(map, tile.coordinates) == 0) return;
            corners[label] = tile;
        }

        private static CityAnalysisResult? EvaluatePoint(
            MapData map,
            TileData cityTile,
            TileData startTile,
            int searchRadius,
            PlayerState currentOwner,
            string label)
        {
            if (startTile == null) return null;

            TileData[] areaTiles = map.GetAreaSorted(
                startTile.coordinates, searchRadius, true, true);

            int enemyCityCount = 0;
            int ownedCityCount = 0;

            if (areaTiles != null)
            {
                foreach (var areaTile in areaTiles)
                {
                    if (areaTile?.improvement == null) continue;
                    if (areaTile.improvement.type != ImprovementData.Type.City) continue;
                    if (areaTile.coordinates.X == cityTile.coordinates.X
                        && areaTile.coordinates.Y == cityTile.coordinates.Y)
                        continue;

                    if (areaTile.owner != currentOwner.Id)
                        enemyCityCount++;
                    else
                        ownedCityCount++;
                }
            }

            return new CityAnalysisResult
            {
                TargetTile = startTile,
                EnemyCityCount = enemyCityCount,
                OwnedCityCount = ownedCityCount,
                TileTypeLabel = label
            };
        }

        private static int CountByFaction(CityAnalysisResult result, Faction findType)
        {
            return findType switch
            {
                Faction.Enemy => result.EnemyCityCount,
                Faction.Owned => result.OwnedCityCount,
                _ => result.EnemyCityCount + result.OwnedCityCount
            };
        }

        public static void LogAnalysisResult(
            TileData cityTile,
            CityAnalysisResult? result,
            int radius,
            Faction findType = Faction.Both,
            bool findMost = true)
        {
            if (result == null)
            {
                Loader.modLogger?.LogWarning(
                    $"[MapAnalysis] No result for city ({cityTile?.coordinates.X}, {cityTile?.coordinates.Y})");
                return;
            }

            string selectionMode = findMost ? "Highest" : "Lowest";
            Loader.modLogger?.LogInfo(
                $"[MapAnalysis] City=({cityTile.coordinates.X},{cityTile.coordinates.Y}) " +
                $"radius={radius} {findType}/{selectionMode} → {result.TileTypeLabel} " +
                $"({result.TargetTile?.coordinates.X},{result.TargetTile?.coordinates.Y}) " +
                $"enemy={result.EnemyCityCount} owned={result.OwnedCityCount}");
        }
    }
}