using System;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSystem.Collections.Generic;
using PolyMod;
using Polytopia.Data;
using PolytopiaBackendBase.Game;
using PolytopiaBackendBase.Common;
using UnityEngine;

namespace PolyMode
{
    public static class CustomAI
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
            CityReward[] rewardarray = City.GetRewardsForLevel(gld.GetImprovementData(tile.improvement.type), tile.improvement.level - 1);

            System.Random random = new System.Random();

            PlayerState playerState;
            if (!gameState.TryGetPlayer(tile.owner, out playerState) || !GameManager.GameState.GameLogicData.TryGetData(playerState.tribe, out TribeData tribeData))
            {
                return;
            }
            if (tile.improvement.level == 2)
            {
                CityAnalysisResult? centerResult = MapAnalysisUtils.ScanCity(gameState.Map, gameState, tile, 3, true, playerState);
                MapAnalysisUtils.LogAnalysisResult(tile, centerResult, 3);

                if (centerResult != null && centerResult.EnemyCityCount >= 2 && centerResult.EnemyCityCount - centerResult.OwnedCityCount >= 2)
                {
                    Loader.modLogger?.LogInfo(
                    $"[Conquest-Evacuation] City at location ({tile.coordinates.X}, {tile.coordinates.Y}) unfavorable. " +
                    $"(Enemies: {centerResult.EnemyCityCount}, Allies: {centerResult.OwnedCityCount}, Gap: {centerResult.EnemyCityCount - centerResult.OwnedCityCount}). " +
                    $"Forcing Evacuation selection!");

                    int num = 2;
                    __result = rewardarray[num];
                }
                else
                {
                    int num = random.Next(0, rewardarray.Length - 1);
                    __result = rewardarray[num];
                }
            }
            else
            if (tile.improvement.level == 4)
            {
                CityAnalysisResult? centerResult = MapAnalysisUtils.ScanCity(gameState.Map, gameState, tile, 8, true, playerState);
                MapAnalysisUtils.LogAnalysisResult(tile, centerResult, 8);

                if (centerResult != null && centerResult.EnemyCityCount == 0 && playerState.cities >= 4)
                {
                    Loader.modLogger?.LogInfo(
                    $"[Conquest-Tax] City at location ({tile.coordinates.X}, {tile.coordinates.Y}) favourable. " +
                    $"(Enemies: {centerResult.EnemyCityCount}). " +
                    $"Forcing Tax Reform selection!");

                    int num = 2;
                    __result = rewardarray[num];
                }
                else
                {
                    int num = random.Next(0, rewardarray.Length - 1);
                    __result = rewardarray[num];
                }
            }
            else
            if (tile.improvement.level >= 5)
            {
                int num = 1;
                __result = rewardarray[num];
            }
            else
            {
                int num = random.Next(0, rewardarray.Length);
                __result = rewardarray[num];
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(AI), nameof(AI.GetImprovementScore))]
        private static void GetImprovementScore_Citadel(GameState gameState, ImprovementData improvementData, TileData tileData, PlayerState player, ref float __result)
        {
            if (gameState == null || tileData == null || player == null) return;
            
            try
            {
                float num = 0;
                TileData rulingCity = gameState.Map.GetTile(tileData.rulingCityCoordinates);
                if (rulingCity != null && rulingCity.improvement != null)
                {
                    int expansionRadius = rulingCity.improvement.borderSize;

                    CityAnalysisResult? bestCornerResult = MapAnalysisUtils.ScanCity(
                        gameState.Map,
                        gameState,
                        rulingCity,
                        5,
                        false, 
                        player,
                        Faction.Both,
                        true                 
                    );

                    if (bestCornerResult != null && bestCornerResult.TargetTile != null)
                    {
                        TileData targetedCornerTile = bestCornerResult.TargetTile;

                        if (tileData.coordinates.X == targetedCornerTile.coordinates.X && 
                            tileData.coordinates.Y == targetedCornerTile.coordinates.Y)
                        {
                            float totalStrategicScore = 0f;

                            // ---- 評估 A：微觀土地擴張可行性 (檢查法定 1 或 2 半徑內真正的無主土地) ----
                            WorldCoordinates centerCoord = new WorldCoordinates(tileData.coordinates.X, tileData.coordinates.Y);
                            TileData[] nearbyTiles = gameState.Map.GetAreaSorted(centerCoord, expansionRadius, true, true);

                            int unclaimedCount = 0;
                            if (nearbyTiles != null)
                            {
                                foreach (var tileInZone in nearbyTiles)
                                {
                                    // 判定是否為未被任何城市統治、此座城堡蓋下去實質可以打包帶走的無主格子
                                    if (tileInZone != null && (tileInZone.owner == 0))
                                    {
                                        unclaimedCount++;
                                    }
                                }
                            }

                            MapAnalysisUtils.LogAnalysisResult(rulingCity, bestCornerResult, 5);

                            // 根據範圍內「實質能吞併」的土地數量給予擴張分數
                            if (unclaimedCount > 0)
                            {
                                float expansionScore = unclaimedCount * 50f; 
                                totalStrategicScore += expansionScore;
                                
                                Loader.modLogger?.LogInfo(
                                    $"[AI-Expansion] Strategic Corner Matched! Corner {bestCornerResult.TileTypeLabel} can successfully claim {unclaimedCount} tiles. " +
                                    $"Adding Expansion Score: +{expansionScore}");
                            }

                            // ---- 評估 B：軍事威脅度 (從 4 格宏觀結果中直接累加) ----
                            if (bestCornerResult.EnemyCityCount > 0 || bestCornerResult.OwnedCityCount > 0)
                            {
                                float militaryScore = bestCornerResult.EnemyCityCount * 100f - bestCornerResult.OwnedCityCount * 50f; 
                                totalStrategicScore += militaryScore;

                                Loader.modLogger?.LogInfo(
                                    $"[AI-Tactics] Frontline Warning! Corner {bestCornerResult.TileTypeLabel} detected {bestCornerResult.EnemyCityCount} enemies within radius 5. " +
                                    $"Adding Military Score: +{militaryScore}");
                            }
                            // 分數匯流注入
                            num += totalStrategicScore;
                        }
                        else
                        {
                            // 非最優前線點，砍分
                            num *= 0.1f;
                        }
                    }
                }
                num *= AI.getPriceFactor(improvementData.cost, player);
                __result = num;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-AI] Error in AddPossibleImprovementCommands_Prefix: {ex}");
            }
        }  
    }
}