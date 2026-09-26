using HarmonyLib;
using PolytopiaBackendBase.Game;
using UnityEngine;
using UnityEngine.UI;
using Polytopia.Data;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime;
using PolyMode;

namespace Rush
{
    public static class UI_2
    {
        public static bool IsRushSelected = false;
        public static bool IsRushPNP = false;

        public static int RushTurnLimit = 0;

        // =========================================================================
        // A. Game Setup Screen
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIHorizontalListData), nameof(UIHorizontalListData.AddItem))]
        public static void AddItem_Rush(UIHorizontalListData __instance, string label, int id)
        {
            if (__instance == null) return;

            try
            {
                if (GameManager.PreliminaryGameSettings.GameType == GameType.SinglePlayer) {
                    if (label != null && label == Localization.Get("gamemode.conquest"))
                    {
                        var labels = __instance.labels;
                        if (labels == null) return;

                        for (int i = 0; i < labels.Count; i++)
                        {
                            if (labels[i] != null && label == Localization.Get("gamemode.rush")) return;
                        }

                        int Id = (int)EnumCache<GameMode>.GetType("rush");
                        __instance.AddItem("Rush", Id);

                        Loader.modLogger?.LogInfo($"[Rush-UI] Added 'Rush' mode to {__instance} in SinglePlay  with ID {Id}");
                    }
                } else if (GameManager.PreliminaryGameSettings.GameType == GameType.Competitive || GameManager.PreliminaryGameSettings.GameType == GameType.Multiplayer || GameManager.PreliminaryGameSettings.GameType == GameType.Matchmaking || GameManager.PreliminaryGameSettings.GameType == GameType.PassAndPlay) {
                    {
                        if (label != null && label  == Localization.Get("gamemode.reign"))
                        {
                            var labels = __instance.labels;
                            if (labels == null) return;

                            for (int i = 0; i < labels.Count; i++)
                            {
                                if (labels[i] != null && label == Localization.Get("gamemode.rush"))
                                    return;
                            }

                            int Id = (int)EnumCache<GameMode>.GetType("rush");
                            __instance.AddItem("Rush", Id);

                            Loader.modLogger?.LogInfo($"[Rush-UI] Added 'Rush' mode to {__instance} in Multi with ID {Id}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Rush-UI] AddItem Postfix error: {ex}");
            }
        }
        
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreen_UI2), nameof(GameSetupScreen_UI2.OnGameModeChanged))]
        public static void OnGameModeChanged_Rush(GameSetupScreen_UI2 __instance, int index)
        {
            if (__instance == null || __instance.view == null) return;
            if (__instance.gameModeData == null || __instance.gameModeData.labels == null) return;

            try
            {
                if (index < 0 || index >= __instance.gameModeData.labels.Count) return;

                string selectedText = __instance.gameModeData.labels[index]?.ToString() ?? "";

                if (selectedText.Equals("Rush", StringComparison.OrdinalIgnoreCase))
                {
                    IsRushSelected = true;
                    RushTurnLimit = 30;
                    GameManager.PreliminaryGameSettings.rules.ScoreLimit = 30;
                    __instance.view.SetShowGameModeDescriptionText("gamemode.rush.description");
                    Loader.modLogger?.LogInfo("[Rush-UI] Rush mode selected (TRUE).");
                }
                else if (!selectedText.Equals("Perfection", StringComparison.OrdinalIgnoreCase))
                {
                    IsRushSelected = false;
                    RushTurnLimit = 0;
                    GameManager.PreliminaryGameSettings.rules.ScoreLimit = 0;
                    __instance.view.SetShowGameModeDescriptionText($"gamemode.{selectedText.ToLowerInvariant()}.description");
                    Loader.modLogger?.LogInfo($"[Rush-UI] Mode changed to: {selectedText} (FALSE).");
                }

                __instance.UpdateLayout();

                if (GameManager.PreliminaryGameSettings.GameType == GameType.SinglePlayer)
                {
                    CreateOpponentsList(__instance);
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning($"[Rush-UI] OnGameModeChanged error: {ex.Message}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreen_UI2), nameof(GameSetupScreen_UI2.OnShow))]
        public static void OnShow_InitLists(GameSetupScreen_UI2 __instance)
        {
            try
            {
                __instance.UpdateLayout();

                if (GameManager.PreliminaryGameSettings.GameType != GameType.SinglePlayer)
                {
                    return;
                }

                Loader.modLogger?.LogInfo($"OnShow memory selected Gamemode ID is {GameManager.PreliminaryGameSettings.RulesGameMode}");

                if (GameManager.PreliminaryGameSettings.RulesGameMode != EnumCache<GameMode>.GetType("rush")) return;
                
                CreateOpponentsList(__instance);
                
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning($"[Rush-UI] OnShow error: {ex.Message}");
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
        public static bool GetMaximumOpponentCount_Rush(int mapSize, MapPreset mapPreset, ref int __result)
        {
            try
            {
                if (GameManager.PreliminaryGameSettings.RulesGameMode != EnumCache<GameMode>.GetType("rush"))
                {
                    return true;
                }

                if (mapSize == 0)
                {
                    __result = 15;
                }

			    __result = (int)Math.Pow((double)(mapSize / 3), 2.0) - 1;
                Loader.modLogger?.LogInfo($"[Rush-Backend] MapSize {mapSize} set. MapType {mapPreset} detected. Limit set to {__result}. Additionally, TurnLimit is {GameManager.PreliminaryGameSettings.rules.ScoreLimit}");
                return true;

            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Rush-Backend] MapDataExtensions error: {ex}");
            }
            return true;
        }

        static UIHorizontalList_UI2? turnLimitList;
        static UIHorizontalListData? turnLimitListData;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreenView), nameof(GameSetupScreenView.Init))]
        static void GameSetupScreenView_Init(
            GameSetupScreenView __instance,
            RectTransform holder)
        {
            try
            {
                if ((GameManager.PreliminaryGameSettings.RulesGameMode == EnumCache<GameMode>.GetType("rush") || IsRushSelected) && turnLimitList == null)
                {
                    turnLimitList = UILibrary.NewHorizontalList(__instance.scroller.content);
                    turnLimitList.ActiveSelf = false;
                    turnLimitList.gameObject.name = "turnLimitList";
                    turnLimitList.OnItemSelected().Add(
                        DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int>>(OnTurnLimitChanged));
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Rush-Setup] Setup Init: {ex}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreenView), nameof(GameSetupScreenView.RunLayout))]
        static void GameSetupScreenView_RunLayout(
            GameSetupScreenView __instance,
            ScreenBase_UI2.ScreenSize screenSize)
        {
            try
            {
                if (!IsRushSelected && GameManager.PreliminaryGameSettings.RulesGameMode != EnumCache<GameMode>.GetType("rush") && turnLimitList != null) {
                    turnLimitList.KillScrollTween();
                    turnLimitList.ActiveSelf = false;
                    
                    if (turnLimitList.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(turnLimitList.gameObject);
                    }
                    
                    turnLimitList = null; 
                    return;
                }
                else if (GameManager.PreliminaryGameSettings.GameType == GameType.SinglePlayer)
                {
                    turnLimitListData = new UIHorizontalListData(3, "Turn Limit");
                    //turnLimitListData.AddItem("20", 1);
                    turnLimitListData.AddItem("30", 2);
                    //turnLimitListData.AddItem("50", 3);
                    if (!(GameManager.PreliminaryGameSettings.RulesGameMode == EnumCache<GameMode>.GetType("perfection")))
                    {
                        GameManager.PreliminaryGameSettings.rules.ScoreLimit = 30;
                    }
                }
                else if (GameManager.PreliminaryGameSettings.GameType == GameType.Matchmaking || GameManager.PreliminaryGameSettings.GameType == GameType.Multiplayer || GameManager.PreliminaryGameSettings.GameType == GameType.PassAndPlay)
                {
                    turnLimitListData = new UIHorizontalListData(3, "Turn Limit");
                    turnLimitListData.AddItem("20", 1);
                    turnLimitListData.AddItem("30", 2);
                    turnLimitListData.AddItem("50", 3);
                    if (!(GameManager.PreliminaryGameSettings.RulesGameMode == EnumCache<GameMode>.GetType("perfection")))
                    {
                        GameManager.PreliminaryGameSettings.rules.ScoreLimit = 30;
                    }
                }

                if ((GameManager.PreliminaryGameSettings.RulesGameMode == EnumCache<GameMode>.GetType("rush") || IsRushSelected) && turnLimitList == null)
                {
                    turnLimitList = UILibrary.NewHorizontalList(__instance.scroller.content);
                    turnLimitList.ActiveSelf = false;
                    turnLimitList.gameObject.name = "turnLimitList";
                    turnLimitList.OnItemSelected().Add(
                        DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int>>(OnTurnLimitChanged));
                }

                if (turnLimitList == null || turnLimitListData == null) return;

                turnLimitList.SetData(
                    turnLimitListData.header,
                    turnLimitListData.GetLabels());
                turnLimitList.SetWidth(screenSize.screenRect.Width);
                turnLimitList.UpdateLayout();
                turnLimitList.SelectedIndex = 0;
                turnLimitList.ActiveSelf = true;

                var dimension = __instance.scroller?.content?.Find("dimensionList");
                if (dimension != null)
                {
                    var Comp = dimension.GetComponent<UIHorizontalList_UI2>();
                    if (Comp != null && Comp.ActiveSelf)
                    {
                        InsertListAt(__instance, turnLimitList, Comp, true, 10f);
                        return;
                    }
                }

                if (__instance.gameConfigurationDescriptionText != null
                    && __instance.gameConfigurationDescriptionText.ActiveSelf)
                {
                    InsertListAt(
                        __instance,
                        turnLimitList,
                        __instance.gameConfigurationDescriptionText,
                        true,
                        -3f);
                }
                else if (__instance.gameModeDescriptionText != null
                    && __instance.gameModeDescriptionText)
                {
                    InsertListAt(
                        __instance,
                        turnLimitList,
                        __instance.gameModeDescriptionText,
                        true,
                        -3f);
                }
                else if (__instance.listGameMode != null && __instance.listGameMode.ActiveSelf)
                {
                    InsertListAt(
                        __instance,
                        turnLimitList,
                        __instance.listGameMode,
                        true,
                        10f);
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Rush-Setup] Setup RunLayout: {ex}");
            }
        }

        static void InsertListAt(
            GameSetupScreenView view,
            UIHorizontalList_UI2 list,
            UIBasicComponent insertAfter,
            bool shouldPush = true,
            float padding = 10f)
        {
            if (insertAfter == null || !insertAfter.ActiveSelf)
            {
                list.ActiveSelf = false;
                return;
            }

            list.ActiveSelf = true;
            float y = insertAfter.GetY() - list.GetHeight() - padding;

            if (shouldPush)
            {
                foreach (var layoutable in view.allComponents)
                {
                    if (layoutable == null)
                        continue;
                    if (layoutable.GetY() <= y
                        && layoutable.Pointer != list.Pointer
                        && layoutable.ActiveSelf)
                    {
                        layoutable.SetY(layoutable.GetY() - list.GetHeight() - 10);
                    }
                }
            }

            list.SetPosition(insertAfter.GetX(), y);
            list.UpdateLayout();
        }

        static void OnTurnLimitChanged(int index)
        {
            if (turnLimitListData == null) return;
            int id = turnLimitListData.GetId(index);
            if (id == 1)
            {
                RushTurnLimit = 20;
                GameManager.PreliminaryGameSettings.rules.ScoreLimit = UI_2.RushTurnLimit;
                //GameManager.PreliminaryGameSettings.rules.ScoreLimit = 600;
            }
            if (id == 2)
            {
                RushTurnLimit = 30;
                GameManager.PreliminaryGameSettings.rules.ScoreLimit = UI_2.RushTurnLimit;
                //GameManager.PreliminaryGameSettings.rules.ScoreLimit = 700;
            }
            if (id == 3)
            {
                RushTurnLimit = 50;
                GameManager.PreliminaryGameSettings.rules.ScoreLimit = UI_2.RushTurnLimit;
                //GameManager.PreliminaryGameSettings.rules.ScoreLimit = 800;
            }
            GameManager.PreliminaryGameSettings.SaveToDisk();
            Loader.modLogger?.LogInfo($"[Rush-Map] TurnLimit is set as {GameManager.PreliminaryGameSettings.rules.ScoreLimit}");
            //Loader.modLogger?.LogInfo($"[Rush-Map] ScoreLimit is set as {GameManager.PreliminaryGameSettings.rules.ScoreLimit}");
        }
        
        // =========================================================================
        // B. Game Stats Screen
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameStatsScreen), nameof(GameStatsScreen.PopulateScreen))]
        public static void PopulateScreen_Rush(GameStatsScreen __instance)
        {
            try
            {
                if (__instance.GameSettings.RulesGameMode != EnumCache<GameMode>.GetType("rush"))
                {
                    return;
                }

                __instance.ClearStatsRows();
                __instance.moreInfoButton.SetData(__instance.PrepareGameInfo());
                //__instance.PopulateStatsList();
                __instance.PopulatePlayers();
                __instance.PopulateTasks();
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Rush-Backend] GameStatsScreen PopulateScreen error: {ex}");
            } 
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameStatsScreen), nameof(GameStatsScreen.GetDescription))]
        public static void GetDescription_Rush(GameStatsScreen __instance, PlayerState player, int cityCount, string userName, ref string __result)
        {
            try
            {
                if (__instance.GameSettings.RulesGameMode != EnumCache<GameMode>.GetType("rush"))
                {
                    return;
                }

                bool isSpectating = GameManager.Client.IsSpectating;
                bool flag = player == GameManager.LocalPlayer && !isSpectating;
                bool autoPlay = player.AutoPlay;
                bool flag2 = player.IsAlive(GameManager.GameState);
                bool flag3 = flag || GameManager.LocalPlayer.KnowsPlayer(player.Id) || !flag2 || (isSpectating && GameManager.LocalPlayer.Id == player.Id) || !autoPlay;
                string arg = string.Empty;
                int starCount = player.Currency;

                if (flag3)
                {
                    arg = Localization.Get("gamestatus.ruled", new Il2CppReferenceArray<Il2CppSystem.Object>(new[] { (Il2CppSystem.Object)userName }));
                }
                else
                {
                    arg = Localization.Get("gamestatus.unknown.ruler", new Il2CppReferenceArray<Il2CppSystem.Object>(0));
                }
                if (!flag2)
                {
                    __result = string.Format("{0}", arg);
                }
                __result = string.Format("{0}, {1}", arg, Localization.Get((starCount > 1) ? "gamestatus.stars" : "gamestatus.star", (Il2CppReferenceArray<Il2CppSystem.Object>)(new[]
                    {
                       (Il2CppSystem.Object)starCount
                    })));
                }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Rush-Backend] GameStatsScreen GetDescription error: {ex}");
            } 
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameState), nameof(GameState.GetPlayersSortedForGameMode))]
        public static void GetPlayersSortedForGameMode_Rush(Il2CppSystem.Collections.Generic.List<PlayerState>? players, GameMode gameMode, bool shouldIgnoreResigns, ref Il2CppSystem.Collections.Generic.List<PlayerState>  __result)
        {
            try
            {
                if (gameMode != EnumCache<GameMode>.GetType("rush")) return;

                players = GetPlayersSortedByRush(players);

                __result = GameState.GetPlayersSortedByElimination(players, shouldIgnoreResigns);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Backend] GetPlayersSortedForGameMode error: {ex}");
            } 
        }

        public static Il2CppSystem.Collections.Generic.List<PlayerState>? GetPlayersSortedByRush(Il2CppSystem.Collections.Generic.List<PlayerState>? players)
        {
            if (players == null || players.Count <= 1) return players;

            int count = players.Count;

            // 使用選擇排序法 (Selection Sort)，直接操作 Il2CppSystem 的 List
            for (int i = 0; i < count - 1; i++)
            {
                int maxIndex = i;

                for (int j = i + 1; j < count; j++)
                {
                    PlayerState candidate = players[j];
                    PlayerState currentMax = players[maxIndex];

                    // 比較邏輯：
                    // 1. 優先比較 currency (較大的排前面)
                    if (candidate.currency > currentMax.currency)
                    {
                        maxIndex = j;
                    }
                    else if (candidate.currency == currentMax.currency)
                    {
                        // 2. 若 currency 相同，則比較 score (較大的排前面)
                        if (candidate.score > currentMax.score)
                        {
                            maxIndex = j;
                        }
                    }
                }

                // 如果找到更適合排在前面的玩家，進行位置交換
                if (maxIndex != i)
                {
                    PlayerState temp = players[i];
                    players[i] = players[maxIndex];
                    players[maxIndex] = temp;
                }
            }

            return players;
        }
        
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameModeButtonWrapper), nameof(GameModeButtonWrapper.SetData))]
        public static void SetData_GamemodeInfo(GameModeButtonWrapper __instance, GameMode summaryGameMode, GameType gameType, int scoreLimit = 10000)
        {
            try
            {
                if (summaryGameMode != EnumCache<GameMode>.GetType("rush"))
                {
                    return;
                }

                __instance.currentGameMode = summaryGameMode;
                __instance.currentGameType = gameType;
                __instance.currentGameRules = new GameRules(__instance.currentGameMode);
                __instance.currentGameRules.ScoreLimit = scoreLimit;

                string modeName = summaryGameMode.GetName();
                __instance.roundButton.text = LocalizationUtils.CapitalizeString(modeName);

                Sprite? ConquestIcon = PolyMod.Registry.GetSprite("rush");
                __instance.roundButton.sprite = ConquestIcon;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Backend] GameModeButtonWrapper error: {ex}");
            } 
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GameModeButtonWrapper), nameof(GameModeButtonWrapper.OnButtonClicked))]
        public static bool OnButtonClicked_GamemodeInfo(GameModeButtonWrapper __instance, int id, UnityEngine.EventSystems.BaseEventData? eventData = null)
        {
            try
            {
                if (__instance.currentGameMode != EnumCache<GameMode>.GetType("rush"))
                {
                    return true;
                }
                
                string modeName = __instance.currentGameMode.GetName();
                string HeaderText = LocalizationUtils.CapitalizeString(modeName);

              	BasicPopup basicPopup = PopupManager.GetBasicPopup();
                basicPopup.Header = HeaderText;

                string? text = null;
                string? text2 = Localization.Get(GameModeUtils.GetDescription(__instance.currentGameMode), (Il2CppReferenceArray<Il2CppSystem.Object>)Array.Empty<Il2CppSystem.Object>());

                if (__instance.currentGameMode == EnumCache<GameMode>.GetType("rush"))
                {
                    text = text2;
                } 
                basicPopup.Description = text;
                basicPopup.buttonData = new PopupBase.PopupButtonData[]
                {
                    new PopupBase.PopupButtonData("buttons.back", PopupBase.PopupButtonData.States.Selected, null, -1, true, null)
                };
                basicPopup.Show(InputManager.GetInputPosition());  

                Loader.modLogger?.LogInfo("[Rush-Backend] OnButtonClicked finished!");

                return false;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Backend] GameModeButtonWrapper error: {ex}");
                return true;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameModeUtils), nameof(GameModeUtils.GetDescription))]
        public static void GetDescription_GamemodeInfo(GameMode gameMode, ref string __result)
        {
            try
            {
                if (gameMode == EnumCache<GameMode>.GetType("rush"))
                {
                    __result = "gamemode.rush.description";
                }
                else
                {
                    __result = string.Empty;
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Rush-Backend] GameModeUtils error: {ex}");
            }
        }

        // =========================================================================
        // C. Improvement Menu (WIP)
        // =========================================================================

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
                        uiroundButton.Cost = improvementData2.cost + num * 10;
                        if (improvementData2.cost <= 0)
                        {
                            uiroundButton.Cost = -1f;
                        }

                        Loader.modLogger?.LogInfo($"[Conquest-Bar] {uiroundButton.name} {uiroundButton.Cost}");
                        
                        uiroundButton.onClickedSignal.Add((System.Action)(() =>
                        {
                            PopupManager.HideCurrentPopup();
                            __instance.ClickedImprovement(buildCommand);
                        }));
                    }
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Bar] AddImprovementButtons error: {ex}");
            }
        }*/

        /*[HarmonyPostfix]
        [HarmonyPatch(typeof(InteractionBar), nameof(InteractionBar.ClickedImprovement))]
        public static void ClickedImprovement_Citadel(InteractionBar __instance, BuildCommand command)
        {
            try
            {
                // Only care about Citadel
                if (command.Type != EnumCache<ImprovementData.Type>.GetType("citadel"))
                    return;

                // Get the popup that was just shown
                IconPopup popup = PopupManager.GetCurrentPopup<IconPopup>();   // or PopupManager.CurrentPopup / ActivePopup depending on version
                Loader.modLogger?.LogInfo($"{popup}");
                if (popup == null) return;

                // Recalculate the real cost
                GameState gameState = GameManager.GameState;
                Tile tile = MapRenderer.Current.GetTileInstance(__instance.coordinates);
                int extra = Main.CountCityCitadel(gameState, tile.Data);

                ImprovementData data;
                if (!gameState.GameLogicData.TryGetData(command.Type, out data) || data == null)
                    return;

                float newCost = data.cost + extra * 10;
                if (data.cost <= 0) newCost = -1f;

                // Apply new cost
                popup.cost = newCost;
                Loader.modLogger?.LogInfo($"{popup.cost}");

                // Force the UI to re-render the cost and button states
                popup.RefreshButtonState();
                popup.Show(InputManager.GetInputPosition());
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Backend] ClickedImprovement error: {ex}");
            }
        }*/

        // =========================================================================
        // D. End Match Reactions
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameOverReaction), nameof(GameOverReaction.GetHeader))]
        public static void GetHeader_Custom(ref string __result)
        {
            try
            {
                if (GameManager.GameState.Settings.RulesGameMode == EnumCache<GameMode>.GetType("conquest"))
                {
                    __result = "gamemode.conquest";
                }
                else if (GameManager.GameState.Settings.RulesGameMode == EnumCache<GameMode>.GetType("reign"))
                {
                    __result = "gamemode.reign";
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Backend] GameOverReaction GetHeader error: {ex}");
            } 
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameOverReaction), nameof(GameOverReaction.GetDescription))]
        public static void GetDescription_Custom(PlayerState winningPlayer, ref string __result)
        {
            try
            {
                GameSettings settings = GameManager.GameState.Settings;
                Il2CppSystem.Collections.Generic.List<PlayerState> playersSortedByRankForMultiplayerResults = GameManager.GameState.GetPlayersSortedByRankForMultiplayerResults();
                bool flag = GameManager.LocalPlayer.Id == playersSortedByRankForMultiplayerResults[0].Id;
                string linkedTribeNameWithSpace = winningPlayer.GetLinkedTribeNameWithSpace(GameManager.GameState);
                
                if (settings.RulesGameMode == EnumCache<GameMode>.GetType("conquest"))
                {
                    if (flag)
                    {
                        __result = Localization.Get("gamemode.conquest.win", Array.Empty<Il2CppSystem.Object>());
                    } else {
                        __result = Localization.Get("gamemode.conquest.loss", Array.Empty<Il2CppSystem.Object>());
                    }
                }
                else if (settings.RulesGameMode == EnumCache<GameMode>.GetType("reign"))
                {
                    if (flag)
                    {
                        Localization.Get(GameStateUtils.SecondLastPlayerResigned(GameManager.GameState) ? "gamemode.reign.win.last.human" : "gamemode.reign.win", Array.Empty<Il2CppSystem.Object>());
                    }
                    else
                    {
                        __result = string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Backend] GameOverReaction GetDescription error: {ex}");
            } 
        }
    }
}