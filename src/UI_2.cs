using HarmonyLib;
using PolytopiaBackendBase.Game;
using UnityEngine;
using System;

namespace Polyquest
{
    // 💡 完美的終極打法：直接 Hook 資料結構本身的 AddItem 函數！
    // 這樣完全不需要去碰 GameSetupScreen_UI2 那些無法被 Patch 的欄位屬性
    public static class UI_2
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIHorizontalListData), nameof(UIHorizontalListData.AddItem))]
        public static void AddItem_Postfix(UIHorizontalListData __instance, string label, int id)
        {
            // 指標與實例安全防護
            if (__instance == null || __instance.Pointer == IntPtr.Zero) return;

            try
            {
                // 根據你發送的日誌，Polytopia 載入的最後一個預設模式是 "Infinity"
                // 當我們捕捉到遊戲剛剛把 "Infinity" 塞進選單的這一萬分之一秒...
                if (label != null && label.Equals("Infinity", StringComparison.OrdinalIgnoreCase))
                {
                    var labels = __instance.labels;
                    if (labels == null || labels.Pointer == IntPtr.Zero) return;

                    // 嚴格比對，防止重複添加
                    for (int i = 0; i < labels.Count; i++)
                    {
                        if (labels[i] != null && labels[i].Equals("Conquest", StringComparison.OrdinalIgnoreCase))
                        {
                            return; // 已經有了，安全退出
                        }
                    }

                    // 💡 借雞生蛋：直接利用當前正在執行的實例，幫它追加第四個選項！
                    // 因為這是在 UI 還沒畫出來、資料正在一筆筆塞入的建立期，
                    // 這樣塞入會完美同步更新 labels 和 ids，完全符合 Polytopia 的原生框架設計。
                    __instance.AddItem("Conquest", 99);
                    
                    Loader.modLogger?.LogInfo($"[Conquest-UI] ✅ SUCCESS: Naturally appended 'Conquest' via AddItem Postfix! Total items now: {labels.Count}");
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-UI] AddItem post detour encountered an issue: {ex.Message}");
            }
        }

        // 保持對按鈕點擊事件的監聽 (完全沿用你最習慣的 nameof 格式)
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
                    Loader.modLogger?.LogInfo($"[Conquest-UI] Interaction Log -> Clicked Mode: '{selectedText}' (Index: {index})");

                    if (selectedText.Equals("Conquest", StringComparison.OrdinalIgnoreCase))
                    {
                        Loader.modLogger?.LogInfo("[Conquest-UI] Action -> Target set to CONQUEST. Enabling game flags.");
                        Loader.SetConquestMode(GameManager.PreliminaryGameSettings, true);
                    }
                    else
                    {
                        if (Loader.IsConquestMode(GameManager.PreliminaryGameSettings))
                        {
                            Loader.modLogger?.LogInfo("[Conquest-UI] Action -> Target moved away. Disabling game flags.");
                            Loader.SetConquestMode(GameManager.PreliminaryGameSettings, false);
                        }
                    }
                }
            }
        }
    }
}
