using HarmonyLib;
using PolytopiaBackendBase.Game;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine.EventSystems;

namespace Polyquest
{
    public static class UI_2
    {
        // internal static bool conquestSelected = false;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameModeScreen_UI2), nameof(GameModeScreen_UI2.OnCustom))]
        private static void GameModeScreen_UI2_OnCustom_Postfix(GameModeScreen_UI2 __instance)
        {
            Loader.modLogger?.LogInfo("[Conquest-UI] GameModeScreen_UI2.OnCustom triggered → Preparing Conquest injection");

            // Delay a bit because GameSetupScreen_UI2 may not be fully initialized yet
            // We can use a coroutine or just run it on next frame, but for simplicity we use LateInvoke
            RunDelayed(() => InjectConquestIntoSetupScreen());
        }

        private static void RunDelayed(Action action)
        {
            Loader.modLogger?.LogInfo("[Conquest-UI] RunDelayed timed...");

            // Simple way: use Unity's coroutine or just call it after a short delay
            // For now, we'll call it directly first (we can improve later if needed)
            action?.Invoke();
        }

        private static void InjectConquestIntoSetupScreen()
        {
            Loader.modLogger?.LogInfo("[Conquest-UI] Looking for GameSetupScreen_UI2 via UIManager...");

            if (UIManager.Instance == null)
            {
                Loader.modLogger?.LogWarning("[Conquest-UI] UIManager.Instance is null");
                return;
            }

            IScreen screen = UIManager.Instance.GetScreen(UIConstants.Screens.GameSetup);
            if (screen == null)
            {
                Loader.modLogger?.LogWarning("[Conquest-UI] GetScreen(GameSetup) returned null");
                return;
            }

            GameSetupScreen_UI2 setupScreen = screen.Cast<GameSetupScreen_UI2>();
            if (setupScreen == null)
            {
                Loader.modLogger?.LogWarning("[Conquest-UI] Cast to GameSetupScreen_UI2 failed");
                return;
            }

            Loader.modLogger?.LogInfo("[Conquest-UI] Successfully got GameSetupScreen_UI2, injecting Conquest...");
            InjectConquestToModeList(setupScreen);
        }

        // Reuse the injection helper
        private static void InjectConquestToModeList(GameSetupScreen_UI2 instance)
        {
            if (instance?.gameModeData == null)
            {
                Loader.modLogger?.LogWarning("[Conquest-UI] gameModeData is null");
                return;
            }

            var targetList = instance.gameModeData.labels;   // 使用 labels

            if (targetList == null)
            {
                Loader.modLogger?.LogWarning("[Conquest-UI] labels is null");
                return;
            }

            // 轉成普通 List 方便操作
            List<string> current = new List<string>();
            for (int i = 0; i < targetList.Count; i++)
            {
                current.Add(targetList[i]);
            }

            if (current.Any(x => x.Equals("Conquest", StringComparison.OrdinalIgnoreCase)))
            {
                Loader.modLogger?.LogInfo("[Conquest-UI] Conquest already in list");
                return;
            }

            current.Add("Conquest");

            // 建立新的 Il2Cpp List 並寫回去
            var newList = new Il2CppSystem.Collections.Generic.List<string>();
            foreach (string s in current)
            {
                newList.Add(s);
            }

            instance.gameModeData.labels = newList;

            Loader.modLogger?.LogInfo($"[Conquest-UI] ✅ Successfully injected 'Conquest'! Total modes: {current.Count}");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreen_UI2), nameof(GameSetupScreen_UI2.OnGameModeChanged))]
        public static void OnGameModeChanged_Postfix(GameSetupScreen_UI2 __instance, int index)
        {
            Loader.modLogger?.LogInfo($"[Conquest-UI] OnGameModeChanged Postfix event captured. Raw index: {index}");
            EvaluateGameSetupScreenState(__instance, index);
        }

        private static void EvaluateGameSetupScreenState(GameSetupScreen_UI2 instance, int index)
        {
            Loader.modLogger?.LogInfo("[Conquest-UI] Inspecting visual UI elements selection components...");

            if (instance.gameModeData == null)
            {
                Loader.modLogger?.LogError("[Conquest-UI] Aborting check: instance.gameModeList is NULL reference.");
                return;
            }

            // int activeVisualIndex = instance.gameModeData.selectedObject;
            Loader.modLogger?.LogInfo($"[Conquest-UI] Current active menu highlighted item index reads: {index}");

            // Loader.modLogger?.LogInfo($"[Conquest-UI] Test Values: {instance.gameModeData.selectedObject}");
            Loader.modLogger?.LogInfo($"[Conquest-UI] Total Label Counts: {instance.gameModeData.labels.Count}");
            
            if (index >= 0 && index < instance.gameModeData.labels.Count)
            {
                var activeItem = instance.gameModeData.labels[index];
                if (activeItem != null && activeItem != null)
                {
                    string selectedText = activeItem.ToString();
                    Loader.modLogger?.LogInfo($"[Conquest-UI] Extracted visual text string from highlighted item slot: '{selectedText}'");

                    // If the text label matches, call the separated custom settings applicator handler
                    if (selectedText.Equals("Conquest", StringComparison.OrdinalIgnoreCase))
                    {
                        Loader.modLogger?.LogInfo("[Conquest-UI] Conquest selected → Setting flag");
                        Loader.SetConquestMode(GameManager.PreliminaryGameSettings, true);
                        Loader.modLogger?.LogInfo("[Conquest-UI] SUCCESS: Conquest mode dictionary flag successfully set to TRUE.");
                    }
                    else
                    {
                        Loader.modLogger?.LogInfo("[Conquest-UI] NOT Conquest selected");
                        if (Loader.IsConquestMode(GameManager.PreliminaryGameSettings))
                        {
                            Loader.modLogger?.LogInfo($"[Conquest-UI] Switched away from Conquest → Resetting flag");
                            Loader.SetConquestMode(GameManager.PreliminaryGameSettings, false);
                            Loader.modLogger?.LogInfo("[Conquest-UI] SUCCESS: Conquest mode dictionary flag successfully set to FALSE.");                        
                        }
                    }
                }
            }
            else
            {
                Loader.modLogger?.LogWarning($"[Conquest-UI] Active index {index} is out of bounds (0 to {instance.gameModeData.labels.Count - 1}). skipping.");
            }
        }
    }
}
