using HarmonyLib;
using PolytopiaBackendBase.Game;
using UnityEngine;
using System;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace PolyMode
{
    public static class UI_2
    {
        public static bool IsConquestSelected = false;
        
        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIHorizontalListData), nameof(UIHorizontalListData.AddItem))]
        public static bool AddItem_Prefix(UIHorizontalListData __instance, string label, int id)
        {
            // 💡 1. 移除不安全的指標判斷（防範 Access Violation），僅保留安全的受管 null 檢查
            if (__instance == null) return true;

            try
            {
                GameSetupScreen_UI2? activeScreen = null;

                var rawScreen = UnityEngine.Object.FindObjectOfType(Il2CppInterop.Runtime.Il2CppType.Of<GameSetupScreen_UI2>());
                
                if (rawScreen != null)
                {
                    activeScreen = rawScreen.TryCast<GameSetupScreen_UI2>();
                }

                if (activeScreen != null && activeScreen.gameModeData != null)
                {
                    int registeredConquestId = PolyMod.Registry.gameModesAutoidx - 1;
                    
                    if (activeScreen.gameModeData.selectedObject != registeredConquestId) 
                    {
                        return true;
                    }
                }
                else
                {
                    return true; 
                }

                if (int.TryParse(label, out int count) && count >= 8 && count <= 15)
                {
                    Loader.modLogger?.LogInfo($"[Conquest-UI] [Auto-Detected] Intercepted {label} opponents button.");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-UI] AddItem Prefix error: {ex}");
                return true;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIHorizontalListData), nameof(UIHorizontalListData.AddItem))]
        public static void AddItem_Postfix(UIHorizontalListData __instance, string label, int id)
        {
            if (__instance == null || __instance.Pointer == IntPtr.Zero) return;

            try
            {
                if (label != null && label.Equals("Infinity", StringComparison.OrdinalIgnoreCase))
                {
                    var labels = __instance.labels;
                    if (labels == null) return;

                    // Check if Conquest already exists
                    for (int i = 0; i < labels.Count; i++)
                    {
                        if (labels[i] != null && labels[i].Equals("Conquest", StringComparison.OrdinalIgnoreCase))
                            return;
                    }

                    int registeredConquestId = PolyMod.Registry.gameModesAutoidx - 1;
                    __instance.AddItem("Conquest", registeredConquestId);

                    Loader.modLogger?.LogInfo($"[Conquest-UI] Added 'Conquest' mode with ID {registeredConquestId}");
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
            if (__instance?.gameModeData?.labels == null) return;

            try
            {
                if (index < 0 || index >= __instance.gameModeData.labels.Count) return;

                string selectedText = __instance.gameModeData.labels[index]?.ToString() ?? "";

                if (selectedText.Equals("Conquest", StringComparison.OrdinalIgnoreCase))
                {
                    IsConquestSelected = true;
                    Loader.modLogger?.LogInfo("[Conquest-UI] Conquest mode selected.");
                }
                else
                {
                    IsConquestSelected = false;
                    Loader.modLogger?.LogInfo($"[Conquest-UI] Mode changed to: {selectedText}");
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning($"[Conquest-UI] OnGameModeChanged error: {ex.Message}");
            }
        }
    }
}