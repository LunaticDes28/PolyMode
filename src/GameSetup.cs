using HarmonyLib;
using PolytopiaBackendBase.Game;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime.InteropTypes.Arrays; 

namespace Polyquest
{
    public static class GameSetup
    {
        // Tracks our modified string layout so OnGameModeChanged can read it instantly
        // private static List<string> cachedGameModes = new List<string>();
        private static bool isConquestSelected = false;

        // ====================== CONQUEST MODE ADDITION ======================

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
            Loader.modLogger?.LogInfo("[Conquest] CreateHorizontalList called");
            if (headerKey == "gamesettings.mode") 
            {
                Loader.modLogger?.LogInfo("[Conquest] List of gamemodes found");

                List<string> list = items.ToList();
                list.Add("Conquest");
                items = list.ToArray();
            }
            return true;
        }

        // ====================== INTERCEPT THE CRASH (PREFIX) ======================
        [HarmonyPrefix]
        [HarmonyPatch(typeof(GameSetupScreen), nameof(GameSetupScreen.OnGameModeChanged))]
        private static bool GameSetupScreen_OnGameModeChanged_Prefix(GameSetupScreen __instance, ref int index)
        {
            Loader.modLogger?.LogInfo("[Conquest] Game mode changed");
            string selectedName = string.Empty;

            // Find the UI component directly from the screen instance
            UIHorizontalList gameModeList = null;
            if (__instance?.rows != null)
            {
                foreach (GameObject row in __instance.rows)
                {
                    if (row != null && row.TryGetComponent<UIHorizontalList>(out var list) && list.HeaderKey == "gamesettings.mode")
                    {
                        gameModeList = list;
                        break;
                    }
                }
            }

            // Read the text layout directly from the active game component
            if (gameModeList != null && gameModeList.items != null && index >= 0 && index < gameModeList.items.Length)
            {
                var targetItem = gameModeList.items[index];
                if (targetItem != null && targetItem.text != null)
                {
                    selectedName = targetItem.text.ToString();
                    Loader.modLogger?.LogInfo("[Conquest] Cached: " + selectedName);
                }
            }

            if (!string.IsNullOrEmpty(selectedName) && selectedName.Equals("Conquest", System.StringComparison.OrdinalIgnoreCase))
            {
                isConquestSelected = true;
                index = 0; // Prevent out-of-bounds native crash
            }
            else
            {
                isConquestSelected = false;
            }

            return true; 
        }

        // ====================== APPLY CONQUEST DATA (POSTFIX) ======================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreen), nameof(GameSetupScreen.OnGameModeChanged))]
        private static void GameSetupScreen_OnGameModeChanged_Postfix(GameSetupScreen __instance, int index)
        {
            if (isConquestSelected)
            {
                Loader.modLogger?.LogInfo("[Conquest] Custom game mode applied successfully.");

                var settings = GameManager.PreliminaryGameSettings;
                if (settings != null)
                {
                    settings.BaseGameMode = EnumCache<GameMode>.GetType("conquest");
                    settings.RulesGameMode = EnumCache<GameMode>.GetType("conquest");
                }
            }
        }

        // ====================== ORIGINAL UI HELPERS ======================

        private static UIHorizontalList? FindHorizontalList(GameSetupScreen screen, string headerKey)
        {
            if (screen?.rows == null) return null;

            // In IL2CPP, screen.rows is an Il2CppReferenceArray and can be iterated directly
            foreach (GameObject row in screen.rows)
            {
                if (row != null && row.TryGetComponent<UIHorizontalList>(out var list) && list.HeaderKey == headerKey)
                {
                    return list;
                }
            }
            return null;
        }

        private static string GetSelectedModeName(GameSetupScreen screen, int index)
        {
            UIHorizontalList? list = FindHorizontalList(screen, "gamesettings.mode");
            
            // Safe bounds-check against the actual visual UI array items
            if (list?.items != null && index >= 0 && index < list.items.Length)
            {
                UIHorizontalListItem selectedItem = list.items[index];
                if (selectedItem != null && selectedItem.text != null)
                {
                    return selectedItem.text.ToString();
                }
            }
            return string.Empty;
        }
    }
}