using HarmonyLib;
using PolytopiaBackendBase.Game;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime.InteropTypes.Arrays; 

namespace Polyquest
{
    public static class GameSetup
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(GameSetupScreen), nameof(GameSetupScreen.CreateHorizontalList))]
        private static bool GameSetupScreen_CreateHorizontalList(
            GameSetupScreen __instance, 
            string headerKey, 
            ref Il2CppStringArray items, 
            Il2CppSystem.Action<int> indexChangedCallback, 
            int selectedIndex, 
            RectTransform parent, 
            int enabledItemCount, 
            Il2CppSystem.Action onClickDisabledItemCallback)
        {
            Loader.modLogger?.LogInfo($"[Conquest-UI] CreateHorizontalList intercepted. Checking headerKey: '{headerKey}'");

            if (headerKey == "gamesettings.mode") 
            {
                Loader.modLogger?.LogInfo("[Conquest-UI] Target game mode row found! Appending 'Conquest' string label...");
                
                List<string> list = items.ToList();
                list.Add("Conquest");
                items = list.ToArray();
                
                Loader.modLogger?.LogInfo($"[Conquest-UI] Conquest text injected successfully. Total item options: {list.Count}");
            }
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreen), nameof(GameSetupScreen.OnGameModeChanged))]
        public static void OnGameModeChanged_Postfix(GameSetupScreen __instance, int index)
        {
            Loader.modLogger?.LogInfo($"[Conquest-UI] OnGameModeChanged Postfix event captured. Raw index: {index}");
            EvaluateGameSetupScreenState(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreen), nameof(GameSetupScreen.OnCustomGameModeChanged))]
        public static void OnCustomGameModeChanged_Postfix(GameSetupScreen __instance, int index)
        {
            Loader.modLogger?.LogInfo($"[Conquest-UI] OnCustomGameModeChanged Postfix event captured. Raw index: {index}");
            EvaluateGameSetupScreenState(__instance);
        }

        private static void EvaluateGameSetupScreenState(GameSetupScreen instance)
        {
            Loader.modLogger?.LogInfo("[Conquest-UI] Inspecting visual UI elements selection components...");

            if (instance.gameModeList == null)
            {
                Loader.modLogger?.LogError("[Conquest-UI] Aborting check: instance.gameModeList is NULL reference.");
                return;
            }

            if (instance.gameModeList.items == null)
            {
                Loader.modLogger?.LogError("[Conquest-UI] Aborting check: instance.gameModeList.items unmanaged pointer array is NULL.");
                return;
            }

            int activeVisualIndex = instance.gameModeList.SelectedIndex;
            Loader.modLogger?.LogInfo($"[Conquest-UI] Current active menu highlighted item index reads: {activeVisualIndex}");

            if (activeVisualIndex >= 0 && activeVisualIndex < instance.gameModeList.items.Count)
            {
                var activeItem = instance.gameModeList.items[activeVisualIndex];
                if (activeItem != null && activeItem.text != null)
                {
                    string selectedText = activeItem.text.ToString();
                    Loader.modLogger?.LogInfo($"[Conquest-UI] Extracted visual text string from highlighted item slot: '{selectedText}'");

                    // If the text label matches, call the separated custom settings applicator handler
                    if (selectedText.Equals("Conquest", StringComparison.OrdinalIgnoreCase))
                    {
                        Loader.modLogger?.LogInfo("[Conquest-UI] TEXT MATCH CONFIRMED! Routing to background settings modifier function...");
                        ApplyConquestBackendSettings(instance);
                    }
                    else
                    {
                        Loader.modLogger?.LogInfo($"[Conquest-UI] Active highlighted text '{selectedText}' does not match 'Conquest'. Mod settings skipped.");
                    }
                }
            }
            else
            {
                Loader.modLogger?.LogWarning($"[Conquest-UI] Active index {activeVisualIndex} is out of bounds (0 to {instance.gameModeList.items.Count - 1}). skipping.");
            }
        }

        private static void ApplyConquestBackendSettings(GameSetupScreen instance)
        {
            Loader.modLogger?.LogInfo("[Conquest-UI] Entering ApplyConquestBackendSettings function layer...");
            
            var settings = GameManager.PreliminaryGameSettings;
            if (settings == null)
            {
                Loader.modLogger?.LogError("[Conquest-UI] Fatal Exception: GameManager.PreliminaryGameSettings is NULL. Cannot apply configurations!");
                return;
            }

            //try
            //{
                Loader.modLogger?.LogInfo("[Conquest-UI] Attempting to bind custom enums onto unmanaged backend parameters cache...");
                
                settings.BaseGameMode = EnumCache<GameMode>.GetType("conquest");
                settings.RulesGameMode = EnumCache<GameMode>.GetType("conquest");
                
                Loader.modLogger?.LogInfo($"[Conquest-UI] SUCCESS: Backend rules configured! BaseGameMode: {settings.BaseGameMode} | RulesGameMode: {settings.RulesGameMode}");
            //}
            //catch (Exception ex)
            //{
                //Loader.modLogger?.LogError($"[Conquest-UI] System Crash intercepted inside settings injection block: {ex}");
            //}
        }
    }
}
