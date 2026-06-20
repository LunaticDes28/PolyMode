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

        // ====================== INTERCEPT THE CRASH ======================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreen), nameof(GameSetupScreen.OnGameModeChanged))]
        private static void GameSetupScreen_OnGameModeChanged_Postfix(GameSetupScreen __instance, int index)
        {
            Loader.modLogger!.LogInfo("[Conquest] Game mode changed");
            Loader.modLogger!.LogInfo(index);

            // Use the explicit game settings to determine the actual game type structure
            int adjustedIndex = index;
            if (GameManager.PreliminaryGameSettings.GameType != GameType.Matchmaking)
            {
                adjustedIndex++;
            }

            // Check if index points outside standard game enum values, indicating your custom mode selection
            if (adjustedIndex >= Enum.GetValues<GameMode>().Length)
            {
                var settings = GameManager.PreliminaryGameSettings;
                if (settings != null)
                {
                    // Directly apply the custom mode from your internal mod configuration
                    settings.BaseGameMode = EnumCache<GameMode>.GetType("conquest");
                    settings.RulesGameMode = EnumCache<GameMode>.GetType("conquest");
                    
                    // Re-draw screen details utilizing the game instance's primary lifecycle method
                    __instance.RefreshInfo();
                    Loader.modLogger!.LogInfo("[Conquest] Custom game mode applied successfully");
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