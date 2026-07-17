using HarmonyLib;
using PolytopiaBackendBase.Game;
using UnityEngine;
using UnityEngine.EventSystems;
using Polytopia.Data;

namespace PolyMode
{
    public static class UI_2
    {
        public static bool IsConquestSelected = false;
        public static bool IsReignSelected = false;

        // =========================================================================
        // A. Pre Game Menu
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIHorizontalListData), nameof(UIHorizontalListData.AddItem))]
        public static void AddItem_GamemodeOptions(UIHorizontalListData __instance, string label, int id)
        {
            if (__instance == null) return;

            try
            {
                if (GameManager.PreliminaryGameSettings.GameType == GameType.SinglePlayer) {
                    if (label != null && label == Localization.Get("gamemode.sandbox"))
                    {
                        var labels = __instance.labels;
                        if (labels == null) return;

                        for (int i = 0; i < labels.Count; i++)
                        {
                            if (labels[i] != null && label == Localization.Get("gamemode.conquest"))
                                return;
                        }

                        int Id = (int)EnumCache<GameMode>.GetType("conquest");
                        __instance.AddItem("Conquest", Id);

                        Loader.modLogger?.LogInfo($"[Conquest-UI] Added 'Conquest' mode to {__instance} in SinglePlay  with ID {Id}");
                    }
                } else if (GameManager.PreliminaryGameSettings.GameType == GameType.PassAndPlay) {
                    {
                        if (label != null && label  == Localization.Get("gamemode.might"))
                        {
                            var labels = __instance.labels;
                            if (labels == null) return;

                            for (int i = 0; i < labels.Count; i++)
                            {
                                if (labels[i] != null && label == Localization.Get("gamemode.reign"))
                                    return;
                            }

                            int Id = (int)EnumCache<GameMode>.GetType("reign");
                            __instance.AddItem("Reign", Id);

                            Loader.modLogger?.LogInfo($"[Conquest-UI] Added 'Reign' mode to {__instance} in PassnPlay with ID {Id}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-UI] AddItem Postfix error: {ex}");
            }
        }
        
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreen_UI2), nameof(GameSetupScreen_UI2.OnGameModeChanged))]
        public static void OnGameModeChanged_Conquest(GameSetupScreen_UI2 __instance, int index)
        {
            if (__instance == null || __instance.view == null) return;
            if (__instance.gameModeData == null || __instance.gameModeData.labels == null) return;

            try
            {
                if (index < 0 || index >= __instance.gameModeData.labels.Count) return;

                string selectedText = __instance.gameModeData.labels[index]?.ToString() ?? "";

                if (selectedText.Equals("Conquest", StringComparison.OrdinalIgnoreCase))
                {
                    IsConquestSelected = true;
                    IsReignSelected = false;
                    Loader.modLogger?.LogInfo("[Conquest-UI] Conquest mode selected (TRUE).");
                }
                else
                if (selectedText.Equals("Reign", StringComparison.OrdinalIgnoreCase))
                {
                    IsConquestSelected = false;
                    IsReignSelected = true;
                    Loader.modLogger?.LogInfo($"[Conquest-UI] Reign mode selected (True).");
                }
                else
                {
                    IsConquestSelected = false;
                    IsReignSelected = false;
                    Loader.modLogger?.LogInfo($"[Conquest-UI] Mode changed to: {selectedText} (FALSE).");
                }
                //GameSetupScreen a = new GameSetupScreen();
                //a.gameModeInfoRow = null;

                CreateOpponentsList(__instance);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning($"[Conquest-UI] OnGameModeChanged error: {ex.Message}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreen_UI2), nameof(GameSetupScreen_UI2.OnShow))]
        public static void OnShow_CreateGamemodeList(GameSetupScreen_UI2 __instance)
        {
            try
            {
                Loader.modLogger?.LogInfo($"OnShow memory selected Gamemode ID is {GameManager.PreliminaryGameSettings.RulesGameMode}");

                if (GameManager.PreliminaryGameSettings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")) return;
                
                CreateOpponentsList(__instance);
                
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning($"[Conquest-UI] OnShow error: {ex.Message}");
            }
        } 
        
        private static void CreateOpponentsList(GameSetupScreen_UI2 instance)
        {
                int allowedMaxOpponents = MapDataExtensions.GetMaximumOpponentCountForMapSize(
                    GameManager.PreliminaryGameSettings.MapSize, 
                    GameManager.PreliminaryGameSettings.mapPreset
                );

                if (allowedMaxOpponents <= 0 || allowedMaxOpponents > 15)
                {
                    allowedMaxOpponents = GameManager.GetMaxOpponents(); 
                }

                Loader.modLogger?.LogInfo($"[Conquest-UI] Active UI reconstruction triggered. Calculated max opponents: {allowedMaxOpponents}");

                var uiLabels = new Il2CppSystem.Collections.Generic.List<string>();
                for (int i = 0; i <= allowedMaxOpponents; i++)
                {
                    uiLabels.Add(i.ToString());
                }

                instance.view.SetShowOpponents("Opponents", uiLabels, allowedMaxOpponents + 1);
        }


        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapDataExtensions), nameof(MapDataExtensions.GetMaximumOpponentCountForMapSize))]
        public static bool GetMaximumOpponentCount_Conquest(int mapSize, MapPreset mapPreset, ref int __result)
        {
            try
            {
                if (GameManager.PreliminaryGameSettings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && GameManager.PreliminaryGameSettings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return true;
                }

                if (mapSize <= 16) // Tiny (11) & Small (14) & Normal (16)
                {
                    __result = 3;
                    Loader.modLogger?.LogInfo($"[Conquest-Backend] MapSize {mapSize} (Tiny/Small) detected. Limit set to {__result}.");
                    return false; 
                }
                if (mapSize <= 20) // Large (18) & Huge (20)
                {
                    __result = 5;
                    Loader.modLogger?.LogInfo($"[Conquest-Backend] MapSize {mapSize} (Normal/Large) detected. Limit set to {__result}.");
                    return false;
                }

                // Massive (30) 
                __result = 7;
                Loader.modLogger?.LogInfo($"[Conquest-Backend] MapSize {mapSize} (Huge/Massive) detected. Limit set to {__result}.");
                return false;

            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Backend] MapDataExtensions error: {ex}");
            }
            return true;
        } 
        
        // =========================================================================
        // B. Ingame Stats Screen
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameModeButtonWrapper), nameof(GameModeButtonWrapper.SetData))]
        public static void SetData_GamemodeInfo(GameModeButtonWrapper __instance, GameMode summaryGameMode, GameType gameType, int scoreLimit = 10000)
        {
            try
            {
                if (GameManager.PreliminaryGameSettings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && GameManager.PreliminaryGameSettings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return;
                }

                __instance.currentGameMode = summaryGameMode;
                __instance.currentGameType = gameType;
                __instance.currentGameRules = new GameRules(__instance.currentGameMode);
                __instance.currentGameRules.ScoreLimit = scoreLimit;

                string modeName = summaryGameMode.GetName();
                __instance.roundButton.text = LocalizationUtils.CapitalizeString(modeName);

                Sprite? ConquestIcon = PolyMod.Registry.GetSprite("conquest");
                __instance.roundButton.sprite = ConquestIcon;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Backend] GameModeButtonWrapper error: {ex}");
            } 
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GameModeButtonWrapper), nameof(GameModeButtonWrapper.OnButtonClicked))]
        public static bool OnButtonClicked_GamemodeInfo(int id, UnityEngine.EventSystems.BaseEventData? eventData = null)
        {
            try
            {
                if (GameManager.PreliminaryGameSettings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && GameManager.PreliminaryGameSettings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return true;
                }
                
                string modeName = GameManager.PreliminaryGameSettings.RulesGameMode.GetName();
                string HeaderText = LocalizationUtils.CapitalizeString(modeName);

              	BasicPopup basicPopup = PopupManager.GetBasicPopup();
                basicPopup.Header = HeaderText;

                string? text = null;
                if (GameManager.PreliminaryGameSettings.RulesGameMode == EnumCache<GameMode>.GetType("conquest"))
                {
                    text = "Raze all the other tribes' city from the face of the Square. Without any trace left.";
                } 
                else if (GameManager.PreliminaryGameSettings.RulesGameMode == EnumCache<GameMode>.GetType("reign"))
                {
                    text = "Game mode: Reign\nRaze all capitals to win";
                }
                basicPopup.Description = text;
                basicPopup.buttonData = new PopupBase.PopupButtonData[]
                {
                    new PopupBase.PopupButtonData("buttons.back", PopupBase.PopupButtonData.States.Selected, null, -1, true, null)
                };
                basicPopup.Show(InputManager.GetInputPosition());  

                Loader.modLogger?.LogInfo("[Conquest-Backend] OnButtonClicked finished!");

                return false;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Backend] GameModeButtonWrapper error: {ex}");
                return true;
            }
        }

        // =========================================================================
        // C. Ingame Interaction Menu (Broken)
        // =========================================================================

        // public delegate void ClickButtonDelegate(int index, System.IntPtr eventDataPtr);

        // private static readonly System.Collections.Generic.List<ClickButtonDelegate> _gcProtector = new();

        /*[HarmonyPostfix]
        [HarmonyPatch(typeof(InteractionBar), nameof(InteractionBar.AddImprovementButtons))]
        public static void AddImprovementButtons_Postfix(InteractionBar __instance, Tile tile)
        {
            try
            {
                PlayerState player = GameManager.LocalPlayer;
                if (player == null || player.AutoPlay) return;

                if (GameManager.PreliminaryGameSettings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && GameManager.PreliminaryGameSettings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return;
                }
                
                GameState gameState = GameManager.GameState;
                GameLogicData gameLogicData = gameState.GameLogicData;
                Il2CppSystem.Collections.Generic.List<CommandBase>.Enumerator enumerator = CommandUtils.GetBuildableImprovements(gameState, player, tile.Data, true).GetEnumerator();
                {
                    while (enumerator.MoveNext())
                    {
                        Loader.modLogger?.LogInfo($"[Conquest-Bar] {enumerator.Current.ToString()}");
                        
                        BuildCommand buildCommand = enumerator.Current.Cast<BuildCommand>();
                        ImprovementData improvementData2;                    
                        gameLogicData.TryGetData(buildCommand.Type, out improvementData2);
                        if (improvementData2 == null) continue;
                        Loader.modLogger?.LogInfo($"[Conquest-Bar] Imp data");

                        if (improvementData2.type != EnumCache<ImprovementData.Type>.GetType("citadel"))
                        {
                            continue;
                        }
                        Loader.modLogger?.LogInfo($"[Conquest-Bar] Citadel button initialization");
        
                        UIRoundButton uiroundButton = __instance.CreateRoundBottomBarButton(Localization.Get("improvement.citadel"), false);
                        if (uiroundButton == null) continue;
                        
                        Sprite? Icon = PolyMod.Registry.GetSprite("citadel");
                        uiroundButton.sprite = Icon;
                        uiroundButton.buttonActive = enumerator.Current.IsValid(gameState);
                        uiroundButton.buttonExpensive = !uiroundButton.buttonActive;

                        int num = Main.CountCityCitadel(gameState, tile.Data);
                        uiroundButton.Cost = improvementData2.cost + num * 2;
                        if (improvementData2.cost <= 0)
                        {
                            uiroundButton.Cost = -1f;
                        }

                        Loader.modLogger?.LogInfo($"[Conquest-Bar] {improvementData2.GetName()} {uiroundButton.Cost}");
                        
                        ClickButtonDelegate clickAction = (index, eventDataPtr) =>
                        {
                            PopupManager.HideCurrentPopup();
                            __instance.ClickedImprovement(buildCommand);
                        };

                        _gcProtector.Add(clickAction);

                        System.IntPtr nativeFuncPtr = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(clickAction);
                        
                        uiroundButton.OnClicked = new UIButtonBase.ButtonAction(nativeFuncPtr);
                    }
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Bar] AddImprovementButtons error: {ex}");
            }
        }*/
    }
}