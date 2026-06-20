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
        private static bool _isProcessingCustomMode = false;

        // ====================== CONQUEST MODE ADDITION ======================

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GameSetupScreen), nameof(GameSetupScreen.CreateHorizontalList))]
        private static bool GameSetupScreen_CreateHorizontalList(
            GameSetupScreen __instance, 
            string headerKey, 
            ref Il2CppStringArray items, 
            ref Il2CppSystem.Action<int> indexChangedCallback, // Added 'ref' to modify the callback target
            int selectedIndex, 
            RectTransform parent, 
            int enabledItemCount, 
            Il2CppSystem.Action onClickDisabledItemCallback)
        {
            if (headerKey == "gamesettings.mode") 
            {
                Loader.modLogger?.LogInfo("[Conquest] Intercepted game settings mode list creation.");

                // 1. Expand the array to include your custom text label layout
                List<string> list = items.ToList();
                list.Add("Conquest");
                items = list.ToArray();

                // Get the exact integer index assigned to our custom mode position
                int customModeIndex = list.Count - 1; 

                // 2. Intercept the callback event router by wrapping it
                var originalCallback = indexChangedCallback;
                indexChangedCallback = new Action<int>((clickedIndex) => 
                {
                    Loader.modLogger?.LogInfo($"[Conquest] UI Callback triggered. Clicked index: {clickedIndex}");

                    // If the user clicked our custom appended index layout item
                    if (clickedIndex == customModeIndex)
                    {
                        ProcessCustomModeSelection(__instance);
                    }
                    else
                    {
                        // Pass standard clicks back to the native game engine safely
                        originalCallback?.Invoke(clickedIndex);
                    }
                });
            }
            return true;
        }

        private static void ProcessCustomModeSelection(GameSetupScreen instance)
        {
            if (_isProcessingCustomMode) return;

            var settings = GameManager.PreliminaryGameSettings;
            if (settings != null)
            {
                try
                {
                    _isProcessingCustomMode = true;

                    // Apply custom configurations directly
                    settings.BaseGameMode = EnumCache<GameMode>.GetType("conquest");
                    settings.RulesGameMode = EnumCache<GameMode>.GetType("conquest");

                    // Force the UI elements to redraw based on our fresh backend state
                    instance.RefreshInfo();
                    Loader.modLogger!.LogInfo("[Conquest] Custom game mode applied successfully via callback hook.");
                }
                catch (Exception ex)
                {
                    Loader.modLogger!.LogError($"[Conquest] Failed to safely refresh UI configurations: {ex}");
                }
                finally
                {
                    _isProcessingCustomMode = false;
                }
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreen), nameof(GameSetupScreen.OnGameModeChanged))]
        public static void OnGameModeChanged_Postfix(GameSetupScreen __instance, int index)
        {
            ProcessGameModeChange(__instance, index);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreen), nameof(GameSetupScreen.OnCustomGameModeChanged))]
        public static void OnCustomGameModeChanged_Postfix(GameSetupScreen __instance, int index)
        {
            ProcessGameModeChange(__instance, index);
        }

        private static void ProcessGameModeChange(GameSetupScreen instance, int index)
        {
            
            Loader.modLogger!.LogInfo($"[Conquest] Game mode changed, raw index: {index}");

            if (_isProcessingCustomMode) return;

            int adjustedIndex = index;
            if (GameManager.PreliminaryGameSettings?.GameType != GameType.Matchmaking)
            {
                adjustedIndex++;
            }

            // Verify index exceeds base game configurations safely
            if (adjustedIndex >= Enum.GetValues<GameMode>().Length)
            {
                var settings = GameManager.PreliminaryGameSettings;
                if (settings != null)
                {
                    try
                    {
                        _isProcessingCustomMode = true;

                        // Safely apply custom enum strings
                        settings.BaseGameMode = EnumCache<GameMode>.GetType("conquest");
                        settings.RulesGameMode = EnumCache<GameMode>.GetType("conquest");

                        // Re-draw screen details safely
                        instance.RefreshInfo();
                        Loader.modLogger!.LogInfo("[Conquest] Custom game mode applied successfully");
                    }
                    catch (Exception ex)
                    {
                        Loader.modLogger!.LogError($"[Conquest] Failed to refresh UI: {ex}");
                    }
                    finally
                    {
                        _isProcessingCustomMode = false;
                    }
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