using HarmonyLib;
using PolytopiaBackendBase.Game;
using UnityEngine;
using System;
using Il2CppInterop.Runtime;

namespace Polyquest
{
    public static class UI_2
    {
        public static bool IsConquestSelected = false;

        // 攔截平鋪式選單賦值，生出第 4 個按鈕
        [HarmonyPrefix]
        [HarmonyPatch(typeof(GameSetupScreen_UI2), nameof(GameSetupScreen_UI2.gameModeData), MethodType.Setter)]
        public static void gameModeData_Setter_Prefix(GameSetupScreen_UI2 __instance, ref UIHorizontalListData value)
        {
            if (__instance == null || value == null || value.Pointer == IntPtr.Zero) return;

            try
            {
                var labels = value.labels;
                if (labels == null || labels.Pointer == IntPtr.Zero) return;

                for (int i = 0; i < labels.Count; i++)
                {
                    if (labels[i] != null && labels[i].Equals("Conquest", StringComparison.OrdinalIgnoreCase))
                    {
                        return; 
                    }
                }

                // 直接綁定你經由 EnumCache 註冊成功的動態合法核心列舉 ID (例如 8)
                int registeredConquestId = PolyMod.Registry.gameModesAutoidx - 1;
                value.AddItem("Conquest", registeredConquestId);
                
                Loader.modLogger?.LogInfo($"[Conquest-UI] ✅ SUCCESS: Appended 'Conquest' linked to registered ID {registeredConquestId}. Total: {value.labels.Count}");
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-UI] UI injection detour crashed: {ex.Message}");
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
