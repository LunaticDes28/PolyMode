using HarmonyLib;
using PolytopiaBackendBase.Game;
using UnityEngine;
using System;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using System.Reflection;

namespace PolyMode
{
    public static class UI_2
    {
        public static bool IsConquestSelected = false;
        public static bool IsReignSelected = false;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIHorizontalListData), nameof(UIHorizontalListData.AddItem))]
        public static void AddItem_Postfix(UIHorizontalListData __instance, string label, int id)
        {
            if (__instance == null) return;

            try
            {
                if (GameManager.PreliminaryGameSettings.GameType == GameType.SinglePlayer) {
                    if (label != null && label.Equals("Infinity", StringComparison.OrdinalIgnoreCase))
                    {
                        var labels = __instance.labels;
                        if (labels == null) return;

                        for (int i = 0; i < labels.Count; i++)
                        {
                            if (labels[i] != null && labels[i].Equals("Conquest", StringComparison.OrdinalIgnoreCase))
                                return;
                        }

                        int Id = (int)EnumCache<GameMode>.GetType("conquest");
                        __instance.AddItem("Conquest", Id);

                        Loader.modLogger?.LogInfo($"[Conquest-UI] Added 'Conquest' mode to {__instance} in SinglePlay  with ID {Id}");
                    }
                } else 
                    if (GameManager.PreliminaryGameSettings.GameType == GameType.PassAndPlay) {
                    {
                        if (label != null && label.Equals("Might", StringComparison.OrdinalIgnoreCase))
                        {
                            var labels = __instance.labels;
                            if (labels == null) return;

                            for (int i = 0; i < labels.Count; i++)
                            {
                                if (labels[i] != null && labels[i].Equals("Reign", StringComparison.OrdinalIgnoreCase))
                                    return;
                            }

                            int Id = (int)EnumCache<GameMode>.GetType("reign");
                            __instance.AddItem("Reign", Id);

                            Loader.modLogger?.LogInfo($"[Conquest-UI] Added 'Reign' mode to {__instance} in PassnPlay with ID {Id}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-UI] AddItem Postfix error: {ex}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreen_UI2), nameof(GameSetupScreen_UI2.OnGameModeChanged))]
        public static void OnGameModeChanged_Postfix(GameSetupScreen_UI2 __instance, int index)
        {
            if (__instance == null || __instance.view == null) return;
            if (__instance.gameModeData == null || __instance.gameModeData.labels == null) return;

            try
            {
                if (index < 0 || index >= __instance.gameModeData.labels.Count) return;

                string selectedText = __instance.gameModeData.labels[index]?.ToString() ?? "";

                if (selectedText.Equals("Conquest", StringComparison.OrdinalIgnoreCase))
                {
                    IsConquestSelected = true;
                    IsReignSelected = false;
                    Loader.modLogger?.LogInfo("[Conquest-UI] Conquest mode selected (TRUE).");
                }
                else
                if (selectedText.Equals("Reign", StringComparison.OrdinalIgnoreCase))
                {
                    IsConquestSelected = false;
                    IsReignSelected = true;
                    Loader.modLogger?.LogInfo($"[Conquest-UI] Reign mode selected (True).");
                }
                else
                {
                    IsConquestSelected = false;
                    IsReignSelected = false;
                    Loader.modLogger?.LogInfo($"[Conquest-UI] Mode changed to: {selectedText} (FALSE).");
                }
                    
                CreateOpponentsList(__instance);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning($"[Conquest-UI] OnGameModeChanged error: {ex.Message}");
            }
        }        

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreen_UI2), nameof(GameSetupScreen_UI2.OnShow))]
        public static void OnShow_Postfix(GameSetupScreen_UI2 __instance)
        {
            try
            {
                Loader.modLogger?.LogInfo($"OnShow memory selected Gamemode ID is {GameManager.PreliminaryGameSettings.RulesGameMode}");

                if (GameManager.PreliminaryGameSettings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")) return;
                
                CreateOpponentsList(__instance);
                
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning($"[Conquest-UI] OnShow error: {ex.Message}");
            }
        } 
        
        private static void CreateOpponentsList(GameSetupScreen_UI2 instance)
        {
                int allowedMaxOpponents = MapDataExtensions.GetMaximumOpponentCountForMapSize(
                    GameManager.PreliminaryGameSettings.MapSize, 
                    GameManager.PreliminaryGameSettings.mapPreset
                );

                if (allowedMaxOpponents <= 0 || allowedMaxOpponents > 15)
                {
                    allowedMaxOpponents = GameManager.GetMaxOpponents(); 
                }

                Loader.modLogger?.LogInfo($"[Conquest-UI] Active UI reconstruction triggered. Calculated max opponents: {allowedMaxOpponents}");

                var uiLabels = new Il2CppSystem.Collections.Generic.List<string>();
                for (int i = 0; i <= allowedMaxOpponents; i++)
                {
                    uiLabels.Add(i.ToString());
                }

                instance.view.SetShowOpponents("Opponents", uiLabels, allowedMaxOpponents + 1);
        }


        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapDataExtensions), nameof(MapDataExtensions.GetMaximumOpponentCountForMapSize))]
        public static bool GetMaximumOpponentCount_Prefix(int mapSize, MapPreset mapPreset, ref int __result)
        {
            try
            {
                if (GameManager.PreliminaryGameSettings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && GameManager.PreliminaryGameSettings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return true;
                }

                if (mapSize <= 17) // Tiny (11) & Small (14) & Normal (16)
                {
                    __result = 3;
                    Loader.modLogger?.LogInfo($"[Conquest-Backend] MapSize {mapSize} (Tiny/Small) detected. Limit set to {__result}.");
                    return false; 
                }
                if (mapSize <= 21) // Large (18) & Huge (20)
                {
                    __result = 5;
                    Loader.modLogger?.LogInfo($"[Conquest-Backend] MapSize {mapSize} (Normal/Large) detected. Limit set to {__result}.");
                    return false;
                }

                // Massive (30) 
                __result = 7;
                Loader.modLogger?.LogInfo($"[Conquest-Backend] MapSize {mapSize} (Huge/Massive) detected. Limit set to {__result}.");
                return false;

            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Backend] MapDataExtensions error: {ex}");
            }
            return true;
        }

        private static MethodInfo? _gameInfoMethod;
        
        static UI_2()
        {
            try
            {
                var type = typeof(GameStatsScreen);

                _gameInfoMethod = AccessTools.Method(type, "PrepareGameInfo");
                
                Loader.modLogger?.LogInfo($"[Reflection-UI_2] Methods resolved: " +
                    $"{_gameInfoMethod != null}");
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Reflection-UI_2] Failed to bind methods: {ex.Message}");
            }
        }       
        
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameModeButtonWrapper), nameof(GameModeButtonWrapper.SetData))]
        public static void SetData_Postfix(GameModeButtonWrapper __instance, GameMode summaryGameMode, GameType gameType, int scoreLimit = 10000)
        {
            __instance.currentGameMode = summaryGameMode;
            __instance.currentGameType = gameType;
            __instance.currentGameRules = new GameRules(__instance.currentGameMode);
            __instance.currentGameRules.ScoreLimit = scoreLimit;

            int registeredConquestId = PolyMod.Registry.gameModesAutoidx - 1;
            Sprite? ConquestIcon = PolyMod.Registry.GetSprite("conquest");

            if ((int)summaryGameMode == registeredConquestId) {
                __instance.roundButton.text = summaryGameMode.GetName();
                __instance.roundButton.sprite = ConquestIcon;
            }
        }
    }
}