using HarmonyLib;
using PolytopiaBackendBase.Game;
using UnityEngine;
using UnityEngine.UI;
using Polytopia.Data;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using PolyMode;
using PolytopiaBackendBase;
using PolytopiaBackendBase.Game.BindingModels;

namespace Rush
{
    public static class Main
    {
        // =========================================================================
        // A. GameMode Settings
        // =========================================================================
[HarmonyPostfix]
[HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.Generate))]
private static void Generate_SetGamemode(GameState state, MapGeneratorSettings settings)
{
    try
    {
        if (GameManager.PreliminaryGameSettings.GameType == GameType.Matchmaking
            || GameManager.PreliminaryGameSettings.GameType == GameType.Multiplayer
            || GameManager.PreliminaryGameSettings.GameType == GameType.PassAndPlay)
            return;

        var ra = EnumCache<GameMode>.GetType("rusha");
        var rb = EnumCache<GameMode>.GetType("rushb");
        var prelim = GameManager.PreliminaryGameSettings.RulesGameMode;

        // Setup always leaves rusha; sticky picks Star vs Army at map gen
        bool isRushSetup = prelim == ra || prelim == rb
            || UI_2.RushGoalId == 1 || UI_2.RushGoalId == 2;

        if (!isRushSetup) return;

        var mode = (UI_2.RushGoalId == 2) ? rb : ra;

        state.Settings.RulesGameMode = mode;
        GameManager.PreliminaryGameSettings.RulesGameMode = mode;

        if (GameManager.PreliminaryGameSettings.rules.ScoreLimit == 0)
            UI_2.RushTurnLimit = 30;
        else
            UI_2.RushTurnLimit = GameManager.PreliminaryGameSettings.rules.ScoreLimit;

        state.Settings.rules.TurnLimit = UI_2.RushTurnLimit;

        Loader.modLogger?.LogInfo(
            $"[Rush-Map] Commit goalId={UI_2.RushGoalId} mode={mode} TurnLimit={state.Settings.rules.TurnLimit}");
    }
    catch (Exception ex)
    {
        Loader.modLogger?.LogError($"[Rush-Map] Generate_SetGamemode: {ex.Message}");
    }
}

        /*[HarmonyPostfix]
        [HarmonyPatch(typeof(GameManager), nameof(GameManager.LoadLevel))]
        private static void LoadLevel_SetGamemode()
        {
            try
            {
                bool isRush = UI_2.IsRushSelected;

                // Loader.modLogger?.LogInfo("[Rush-Map] Rush Mode selected!");

                // Pseudo GameSettings in GameState
                if (isRush || GameManager.PreliminaryGameSettings.RulesGameMode == EnumCache<GameMode>.GetType("rush")) 
                {
                    Loader.modLogger?.LogInfo($"[Rush-Map] At least we detected sth");

                    GameManager.Client.GameState.Settings.RulesGameMode = EnumCache<GameMode>.GetType("rush");
                    Loader.modLogger?.LogInfo($"[Rush-Map] RulesGameMode stamped as ID: {(int)GameManager.Client.GameState.Settings.RulesGameMode}");

                    if (GameManager.PreliminaryGameSettings.rules.ScoreLimit == 0)
                    {
                        UI_2.RushTurnLimit = 30;
                    }
                    else
                    {
                        UI_2.RushTurnLimit = GameManager.PreliminaryGameSettings.rules.ScoreLimit;
                    }
                    GameManager.Client.GameState.Settings.rules.TurnLimit = UI_2.RushTurnLimit;
                    Loader.modLogger?.LogInfo($"[Rush-Map] TurnLimit is set as {GameManager.Client.GameState.Settings.rules.TurnLimit}");

                    UI_2.IsRushSelected = false;
                    Loader.modLogger?.LogInfo($"[Rush-Map] Flag IsRushSelected is set {UI_2.IsRushSelected}");               
                } 
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Rush-Map] GameStateUtils error: {ex.Message}");
            }
        }*/

        /// <summary>
        /// Lobby settings: overwrite GameMode with sticky Goal (rusha/rushb).
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(BackendAdapter), nameof(BackendAdapter.CreateLobby))]
        static void CreateLobby_CommitRush(CreateLobbyBindingModel model)
        {
            if (model == null) return;
            if (UI_2.RushGoalId != 1 && UI_2.RushGoalId != 2) return;

            model.GameMode = UI_2.GetCommittedRushMode(); // rusha or rushb
            if (UI_2.RushTurnLimit > 0)
                model.ScoreLimit = UI_2.RushTurnLimit;

            Loader.modLogger?.LogInfo(
                $"[Rush-Lobby] CreateLobby → GameMode={model.GameMode} ScoreLimit={model.ScoreLimit} goalId={UI_2.RushGoalId}");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSettings), nameof(GameSettings.ApplyLobbySettings))]
        private static void ApplyLobbySettings_Rush(GameSettings __instance, LobbyGameViewModel lobbyGameViewModel)
        {
            try
            {
                __instance.LiveGamePreset = !lobbyGameViewModel.IsPersistent;
                __instance.GameType = (GameType)1;
                __instance.GameName = lobbyGameViewModel.Name;
                __instance.MapSize = lobbyGameViewModel.MapSize;
                __instance.mapPreset = lobbyGameViewModel.MapPreset;
                __instance.BaseGameMode = lobbyGameViewModel.GameMode;
                var ra = EnumCache<GameMode>.GetType("rusha");
                var rb = EnumCache<GameMode>.GetType("rushb");
                if (lobbyGameViewModel.GameMode == ra || lobbyGameViewModel.GameMode == rb)
                {
                    __instance.RulesGameMode = lobbyGameViewModel.GameMode;
                }
                if (lobbyGameViewModel.ScoreLimit >= 500)
                {
                    __instance.rules.ScoreLimit = lobbyGameViewModel.ScoreLimit;
                }
                if (lobbyGameViewModel.ScoreLimit < 500)
                {
                    __instance.rules.TurnLimit = lobbyGameViewModel.ScoreLimit;
                }
                __instance.disabledTribes = new Il2CppSystem.Collections.Generic.List<PolytopiaBackendBase.Common.TribeType>();
                for (int i = 0; i < lobbyGameViewModel.DisabledTribes.Count; i++)
                {
                    __instance.disabledTribes.Add((PolytopiaBackendBase.Common.TribeType)lobbyGameViewModel.DisabledTribes[i]);
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Rush-Map] GameStateUtils error: {ex.Message}");
            }
        }
        
        // =========================================================================
        // B. Win Conditions
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameState), nameof(GameState.TryGetWinner))]
        private static void TryGetWinner_Rush(GameState __instance, ref bool __result, ref PlayerState winner)
        {
            if (__result) return;

            if (__instance == null || __instance.Settings == null) return;

            try
            {
                var playersSortedByRank = __instance.GetPlayersSortedByRank();
                if (playersSortedByRank == null || playersSortedByRank.Count == 0) return;

                PlayerState topWinner = playersSortedByRank[0];
                if (topWinner == null) return;

                if (__instance.Settings.RulesGameMode == EnumCache<GameMode>.GetType("rusha")
                    || __instance.Settings.RulesGameMode == EnumCache<GameMode>.GetType("rushb"))
                {
                    int num = GameStateUtils.CountAlivePlayers(__instance); 

                    if (num <= 1)
                    {
                        winner = topWinner;
                        __result = true;
                        return;
                    }

                    if (__instance.Settings.rules.TurnLimit > 0)
                    {
                        __result = (ulong)__instance.CurrentTurn >= (ulong)((long)__instance.Settings.rules.TurnLimit);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest] Error in TryGetWinner Postfix: {ex}");
            }
        }
    }
}