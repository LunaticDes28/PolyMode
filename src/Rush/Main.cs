using System;
using HarmonyLib;
using PolytopiaBackendBase.Game;
using Polytopia.Data;
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
                if (GameManager.PreliminaryGameSettings.GameType == GameType.Matchmaking || GameManager.PreliminaryGameSettings.GameType == GameType.Multiplayer)
                    return;

                var ra = EnumCache<GameMode>.GetType("rusha");
                var rb = EnumCache<GameMode>.GetType("rushb");
                var rc = EnumCache<GameMode>.GetType("rushc");
                var prelim = GameManager.PreliminaryGameSettings.RulesGameMode;

                // Setup leaves rusha; sticky goal commits Star / Army / Tech at map gen
                bool isRushSetup = prelim == ra || prelim == rb || prelim == rc
                    || UI_2.RushGoalId == 1
                    || UI_2.RushGoalId == 2
                    || UI_2.RushGoalId == 3;

                if (!isRushSetup) return;

                GameMode mode;
                if (UI_2.RushGoalId == 3) mode = rc;
                else if (UI_2.RushGoalId == 2) mode = rb;
                else mode = ra;

                state.Settings.RulesGameMode = mode;
                GameManager.PreliminaryGameSettings.RulesGameMode = mode;

                if (GameManager.PreliminaryGameSettings.rules.ScoreLimit == 0)
                    UI_2.RushTurnLimit = 30;
                else
                    UI_2.RushTurnLimit = GameManager.PreliminaryGameSettings.rules.ScoreLimit;

                state.Settings.rules.TurnLimit = UI_2.RushTurnLimit;

                // Fresh match → clear monument research counters
                AI_2.ClearTechSession();

                Loader.modLogger?.LogInfo(
                    $"[Rush-Map] Commit goalId={UI_2.RushGoalId} mode={mode} TurnLimit={state.Settings.rules.TurnLimit}");
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Rush-Map] Generate_SetGamemode: {ex.Message}");
            }
        }

        /// <summary>
        /// Lobby: overwrite GameMode with sticky Goal (rusha / rushb / rushc).
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(BackendAdapter), nameof(BackendAdapter.CreateLobby))]
        static void CreateLobby_CommitRush(CreateLobbyBindingModel model)
        {
            if (model == null) return;
            if (UI_2.RushGoalId < 1 || UI_2.RushGoalId > 3) return;

            model.GameMode = UI_2.GetCommittedRushMode();
            if (UI_2.RushTurnLimit > 0)
                model.ScoreLimit = UI_2.RushTurnLimit;

            Loader.modLogger?.LogInfo(
                $"[Rush-Lobby] CreateLobby → GameMode={model.GameMode} ScoreLimit={model.ScoreLimit} goalId={UI_2.RushGoalId}");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSettings), nameof(GameSettings.ApplyLobbySettings))]
        private static void ApplyLobbySettings_Rush(
            GameSettings __instance, LobbyGameViewModel lobbyGameViewModel)
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
                var rc = EnumCache<GameMode>.GetType("rushc");
                if (lobbyGameViewModel.GameMode == ra
                    || lobbyGameViewModel.GameMode == rb
                    || lobbyGameViewModel.GameMode == rc)
                {
                    __instance.RulesGameMode = lobbyGameViewModel.GameMode;
                }

                if (lobbyGameViewModel.ScoreLimit >= 500)
                    __instance.rules.ScoreLimit = lobbyGameViewModel.ScoreLimit;
                if (lobbyGameViewModel.ScoreLimit < 500)
                    __instance.rules.TurnLimit = lobbyGameViewModel.ScoreLimit;

                __instance.disabledTribes =
                    new Il2CppSystem.Collections.Generic.List<PolytopiaBackendBase.Common.TribeType>();
                for (int i = 0; i < lobbyGameViewModel.DisabledTribes.Count; i++)
                {
                    __instance.disabledTribes.Add(
                        (PolytopiaBackendBase.Common.TribeType)lobbyGameViewModel.DisabledTribes[i]);
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Rush-Map] ApplyLobbySettings: {ex.Message}");
            }
        }

        // =========================================================================
        // B. Win Conditions
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameState), nameof(GameState.TryGetWinner))]
        private static void TryGetWinner_Rush(
            GameState __instance, ref bool __result, ref PlayerState winner)
        {
            if (__result) return;
            if (__instance == null || __instance.Settings == null) return;

            try
            {
                var ra = EnumCache<GameMode>.GetType("rusha");
                var rb = EnumCache<GameMode>.GetType("rushb");
                var rc = EnumCache<GameMode>.GetType("rushc");
                var mode = __instance.Settings.RulesGameMode;

                if (mode != ra && mode != rb && mode != rc)
                    return;

                // Tech: instant win if anyone has full tribe tech tree
                if (mode == rc)
                {
                    PlayerState? bestAll = null;
                    int bestN = -1;
                    foreach (var p in __instance.PlayerStates)
                    {
                        if (p == null || p.Id == 0 || p.Id == 255) continue;
                        if (!p.IsAlive(__instance)) continue;
                        if (!AI_2.HasAllTech(__instance, p)) continue;

                        int n = AI_2.TechCount(p);
                        if (n > bestN)
                        {
                            bestN = n;
                            bestAll = p;
                        }
                    }

                    if (bestAll != null)
                    {
                        winner = bestAll;
                        __result = true;
                        return;
                    }
                }

                var playersSortedByRank = __instance.GetPlayersSortedByRank();
                if (playersSortedByRank == null || playersSortedByRank.Count == 0)
                    return;

                PlayerState topWinner = playersSortedByRank[0];
                if (topWinner == null) return;

                int num = GameStateUtils.CountAlivePlayers(__instance);
                if (num <= 1)
                {
                    winner = topWinner;
                    __result = true;
                    return;
                }

                if (__instance.Settings.rules.TurnLimit > 0)
                {
                    bool timeUp =
                        (ulong)__instance.CurrentTurn
                        >= (ulong)((long)__instance.Settings.rules.TurnLimit);
                    if (timeUp)
                    {
                        winner = topWinner;
                        __result = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Rush] Error in TryGetWinner Postfix: {ex}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameLogicData), nameof(GameLogicData.GetTechPrice))]
        private static void GetTechPrice_Rushc(GameLogicData __instance, TechData techData, PlayerState playerState, GameState state, ref int __result)
        {
            if (state == null || techData == null) return;
            try
            {
                if (GameManager.GameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("rushc"))
                {
                    return;
                };

                if (techData == null || techData.cost == 0)
                {
                    __result = 0;
                }
                float num = 4f;
                num += (float)techData.cost;
                int cities = playerState.cities;
                float delayedTurn = Math.Max((float)state.CurrentTurn - 5, 0);
                float adjustedTurn = Math.Min((float)delayedTurn, 15);
                num += (float)((cities - 1) * techData.cost * (1 + adjustedTurn * 0.04));
                if (__instance.HasAbility(playerState, PlayerAbility.Type.Literacy))
                {
                    float num2 = 0.66666f;
                    num *= num2;
                }
                __result =  (int)Math.Ceiling((double)num);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Tech] Error: {ex.Message}");
            }
        }
    }
}