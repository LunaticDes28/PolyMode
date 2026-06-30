using HarmonyLib;
using PolytopiaBackendBase.Game;
using UnityEngine;
using System;

namespace Polyquest
{
    public static class UI_2
    {
        // 💡 沿用你最習慣的完美 nameof 格式！
        // 攔截 gameModeData 被賦值的瞬間 (Setter Prefix)
        [HarmonyPrefix]
        [HarmonyPatch(typeof(GameSetupScreen_UI2), nameof(GameSetupScreen_UI2.gameModeData), MethodType.Setter)]
        public static void gameModeData_Setter_Prefix(GameSetupScreen_UI2 __instance, ref UIHorizontalListData value)
        {
            // 指標與實例安全防護
            if (__instance == null || value == null || value.Pointer == IntPtr.Zero) return;

            try
            {
                var labels = value.labels;
                if (labels == null || labels.Pointer == IntPtr.Zero) return;

                // 1. 嚴格比對，防止重複添加
                for (int i = 0; i < labels.Count; i++)
                {
                    if (labels[i] != null && labels[i].Equals("Conquest", StringComparison.OrdinalIgnoreCase))
                    {
                        return; // 已經有了，安全放行
                    }
                }

                // 2. 🔥 終極殺招：調用 Polytopia 原生的 AddItem 函數！
                // 這會自動幫我們同時填充 labels 和 ids 兩個內部列表，確保數據鏈完美對稱。
                // 這裡我們給 Conquest 一個自訂的 ID：99 (可根據需求微調，通常不與原生的 0,1,2 重複即可)
                value.AddItem("Conquest", 99);
                
                Loader.modLogger?.LogInfo($"[Conquest-UI] ✅ SUCCESS: Naturally appended 'Conquest' using native AddItem! New Total: {value.labels.Count}");
                
                // 註：因為這發生在資料被推給 UI 表現層的前一刻，
                // Polytopia 的 view 會自動把它當作原生第 4 個選項，在畫面上完整生成出第 4 個點擊按鈕！
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-UI] Native AddItem detour crashed: {ex.Message}");
            }
        }

        // 3. 保持對按鈕切換/點擊事件的監聽 (完全沿用 nameof 格式)
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreen_UI2), nameof(GameSetupScreen_UI2.OnGameModeChanged))]
        public static void OnGameModeChanged_Postfix(GameSetupScreen_UI2 __instance, int index)
        {
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
                    Loader.modLogger?.LogInfo($"[Conquest-UI] Player clicked menu button text: '{selectedText}' (Index: {index})");

                    if (selectedText.Equals("Conquest", StringComparison.OrdinalIgnoreCase))
                    {
                        Loader.modLogger?.LogInfo("[Conquest-UI] Matches 'Conquest' -> Enabling global backend game settings.");
                        Loader.SetConquestMode(GameManager.PreliminaryGameSettings, true);
                    }
                    else
                    {
                        if (Loader.IsConquestMode(GameManager.PreliminaryGameSettings))
                        {
                            Loader.modLogger?.LogInfo("[Conquest-UI] Moved away -> Disabling global backend game settings.");
                            Loader.SetConquestMode(GameManager.PreliminaryGameSettings, false);
                        }
                    }
                }
            }
        }
    }
}
