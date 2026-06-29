using HarmonyLib;
using PolytopiaBackendBase.Game;
using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections; // 必須引用以支援 IEnumerator
using BepInEx.Unity.IL2CPP.Utils; // 確保有引用 BepInEx 提供的協程擴充 (如果編譯錯誤可移除此行)

namespace Polyquest
{
    public static class UI_2
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreen_UI2), nameof(GameSetupScreen_UI2.Init))]
        public static void Init_Postfix(GameSetupScreen_UI2 __instance)
        {
            Loader.modLogger?.LogInfo("[Conquest-UI] GameSetupScreen initialized. Preparing custom data insertion...");
            
            // 關鍵修改：不直接呼叫，而是轉型成 MonoBehaviour 來啟動協程延遲處理
            var monoBehaviour = __instance.Cast<MonoBehaviour>();
            if (monoBehaviour != null)
            {
                monoBehaviour.StartCoroutine(DelayInject(__instance));
                Loader.modLogger?.LogInfo("[Conquest-UI] Delayed injection coroutine started.");
            }
            else
            {
                Loader.modLogger?.LogError("[Conquest-UI] CRITICAL: Cannot cast instance to MonoBehaviour!");
            }
        }

        private static IEnumerator DelayInject(GameSetupScreen_UI2 instance)
        {
            // 延遲等待 1 幀（或是寫 yield return new WaitForSeconds(0.1f);）
            // 確保 Polytopia 自己的 Init 跑完，且 gameModeData 的原生記憶體指標被賦值
            yield return null; 

            Loader.modLogger?.LogInfo("[Conquest-UI] Coroutine awake. Processing InjectConquest securely...");
            InjectConquest(instance);
        }

        private static void InjectConquest(GameSetupScreen_UI2 instance)
        {
            if (instance == null)
            {
                Loader.modLogger?.LogWarning("[Conquest-UI] Injection skipped: instance is NULL.");
                return;
            }

            // 分步安全檢查，避免一碰 null 就直接 Silent Crash
            if (instance.gameModeData == null)
            {
                Loader.modLogger?.LogWarning("[Conquest-UI] Injection aborted: instance.gameModeData is NULL at this frame.");
                return;
            }

            var labels = instance.gameModeData.labels;
            if (labels == null)
            {
                Loader.modLogger?.LogWarning("[Conquest-UI] Injection aborted: gameModeData.labels list is NULL.");
                return;
            }

            // 嚴格比對防重複
            for (int i = 0; i < labels.Count; i++)
            {
                if (labels[i] != null && labels[i].Equals("Conquest", StringComparison.OrdinalIgnoreCase))
                {
                    Loader.modLogger?.LogInfo("[Conquest-UI] Conquest option already detected. Skipping injection.");
                    return;
                }
            }

            // 成功寫入 IL2CPP List
            instance.gameModeData.labels.Add("Conquest");
            Loader.modLogger?.LogInfo($"[Conquest-UI] ✅ Successfully appended Conquest option to GameModeData. Total: {labels.Count}");

            ForceRefreshUI(instance);  
        }

        private static void ForceRefreshUI(GameSetupScreen_UI2 instance)
        {
            try
            {
                Canvas.ForceUpdateCanvases();
                
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
