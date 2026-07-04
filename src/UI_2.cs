using HarmonyLib;
using PolytopiaBackendBase.Game;
using UnityEngine;
using System;
using Il2CppInterop.Runtime;

namespace PolyMode
{
    public static class UI_2
    {
        public static bool IsConquestSelected = false;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIHorizontalListData), nameof(UIHorizontalListData.AddItem))]
        public static bool AddItem_Prefix(UIHorizontalListData __instance, string label, int id)
        {
            if (__instance == null || __instance.Pointer == IntPtr.Zero) return true;

            try
            {
                if (!IsConquestSelected) return true;

                // Intercept at the timing when 13/14/15/16 opponents are being registered
                if (label != null 
                    && (label.Equals("13") || label.Equals("14") || label.Equals("15") || label.Equals("16")))
                {
                    Loader.modLogger?.LogInfo($"[Conquest-UI] ✅ SUCCESS: Manually intercepted {label} button via AddItem Prefix!.");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-UI] AddItem pre detour encountered an issue: {ex.Message}");
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
                // Intercept at the timing the last vanilla gamemode (Infinity) is being registered
                if (label != null && label.Equals("Infinity", StringComparison.OrdinalIgnoreCase))
                {
                    var labels = __instance.labels;
                    if (labels == null || labels.Pointer == IntPtr.Zero) return;

                    for (int i = 0; i < labels.Count; i++)
                    {
                        if (labels[i] != null && labels[i].Equals("Conquest", StringComparison.OrdinalIgnoreCase))
                        {
                            return; 
                        }
                    }

                    int registeredConquestId = PolyMod.Registry.gameModesAutoidx - 1;

                    __instance.AddItem("Conquest", registeredConquestId);
                    
                    Loader.modLogger?.LogInfo($"[Conquest-UI] ✅ SUCCESS: Naturally appended 'Conquest' button via AddItem Postfix! Linked to dynamic ID {registeredConquestId}.");
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-UI] AddItem post detour encountered an issue: {ex.Message}");
            }
        }

        // 監聽點擊事件
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreen_UI2), nameof(GameSetupScreen_UI2.OnGameModeChanged))]
        public static void OnGameModeChanged_Postfix(GameSetupScreen_UI2 __instance, int index)
        {
            if (__instance.gameModeData == null || __instance.gameModeData.labels == null) return;

            try
            {
                if (index >= 0 && index < __instance.gameModeData.labels.Count)
                {
                    var activeItem = __instance.gameModeData.labels[index];
                    if (activeItem != null)
                    {
                        string selectedText = activeItem.ToString();
                        
                        if (selectedText.Equals("Conquest", StringComparison.OrdinalIgnoreCase))
                        {
                            IsConquestSelected = true;
                            Loader.modLogger?.LogInfo("[Conquest-UI] Conquest clicked. Isolated global variable 'IsConquestSelected' set to TRUE.");
                        }
                        else
                        {
                            IsConquestSelected = false;
                            Loader.modLogger?.LogInfo($"[Conquest-UI] Other mode clicked ({selectedText}). Isolated global variable 'IsConquestSelected' set to FALSE.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning($"[Conquest-UI] Selection logger exception: {ex.Message}");
            }
        }
    }
}
