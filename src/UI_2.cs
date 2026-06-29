using HarmonyLib;
using PolytopiaBackendBase.Game;
using UnityEngine;
using UnityEngine.UI; // Required for LayoutRebuilder
using System;
using Il2CppInterop.Runtime;

namespace Polyquest
{
    public static class UI_2
    {
        // 1. Target the Screen Initialization instead of the Option Swap
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreen_UI2), nameof(GameSetupScreen_UI2.Init))]
        public static void Init_Postfix(GameSetupScreen_UI2 __instance)
        {
            Loader.modLogger?.LogInfo("[Conquest-UI] GameSetupScreen initialized. Preparing custom data insertion...");
            InjectConquest(__instance);
        }

        private static void InjectConquest(GameSetupScreen_UI2 instance)
        {
            if (instance == null || instance.gameModeData?.labels == null) return;

            var labels = instance.gameModeData.labels;

            // Strict duplication protection
            for (int i = 0; i < labels.Count; i++)
            {
                if (labels[i] != null && labels[i].Equals("Conquest", StringComparison.OrdinalIgnoreCase))
                {
                    Loader.modLogger?.LogInfo("[Conquest-UI] Conquest option already detected. Skipping injection.");
                    return;
                }
            }

            // Inject into IL2CPP List structure
            instance.gameModeData.labels.Add("Conquest");
            Loader.modLogger?.LogInfo($"[Conquest-UI] ✅ Successfully appended Conquest option to GameModeData.");

            // Recalculate visual components immediately
            ForceRefreshUI(instance);  
        }

        private static void ForceRefreshUI(GameSetupScreen_UI2 instance)
        {
            try
            {
                // 1. 強制更新全域畫布
                Canvas.ForceUpdateCanvases();
                
                // 2. 轉型成 UnityEngine.Component 來獲取 RectTransform
                var component = instance.Cast<UnityEngine.Component>();
                if (component != null)
                {
                    var rectTransform = component.GetComponent<UnityEngine.RectTransform>();
                    if (rectTransform != null)
                    {
                        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
                    }
                }

                Loader.modLogger?.LogInfo("[Conquest-UI] Canvas & Local UI Layout parameters successfully flushed.");
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning($"[Conquest-UI] Visual structural refresh encountered an exception: {ex.Message}");
            }
        }

        // 2. Keep this method isolated purely to track state changes
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreen_UI2), nameof(GameSetupScreen_UI2.OnGameModeChanged))]
        public static void OnGameModeChanged_Postfix(GameSetupScreen_UI2 __instance, int index)
        {
            Loader.modLogger?.LogInfo($"[Conquest-UI] GameMode updated to structural slot index: {index}");
            EvaluateGameSetupScreenState(__instance, index);
        }

        private static void EvaluateGameSetupScreenState(GameSetupScreen_UI2 instance, int index)
        {
            if (instance.gameModeData == null || instance.gameModeData.labels == null) return;

            if (index >= 0 && index < instance.gameModeData.labels.Count)
            {
                var activeItem = instance.gameModeData.labels[index];
                if (activeItem != null)
                {
                    string selectedText = activeItem.ToString();
                    Loader.modLogger?.LogInfo($"[Conquest-UI] Highlighted item label parsed: '{selectedText}'");

                    if (selectedText.Equals("Conquest", StringComparison.OrdinalIgnoreCase))
                    {
                        Loader.modLogger?.LogInfo("[Conquest-UI] Match Found → Enabling custom global backend settings");
                        Loader.SetConquestMode(GameManager.PreliminaryGameSettings, true);
                    }
                    else
                    {
                        if (Loader.IsConquestMode(GameManager.PreliminaryGameSettings))
                        {
                            Loader.modLogger?.LogInfo("[Conquest-UI] Mode shifted away → Clearing custom backend flags");
                            Loader.SetConquestMode(GameManager.PreliminaryGameSettings, false);
                        }
                    }
                }
            }
        }
    }
}