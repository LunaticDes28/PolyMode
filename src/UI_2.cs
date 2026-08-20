using HarmonyLib;
using PolytopiaBackendBase.Game;
using UnityEngine;
using UnityEngine.UI;
using Polytopia.Data;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace PolyMode
{
    public static class UI_2
    {
        public static bool IsConquestSelected = false;
        public static bool IsReignSelected = false;

        // =========================================================================
        // A. Game Setup Screen
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
                } else if (GameManager.PreliminaryGameSettings.GameType == GameType.Competitive || GameManager.PreliminaryGameSettings.GameType == GameType.Multiplayer || GameManager.PreliminaryGameSettings.GameType == GameType.Matchmaking || GameManager.PreliminaryGameSettings.GameType == GameType.PassAndPlay) {
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
                    __instance.view.SetShowGameModeDescriptionText("gamemode.conquest.description");
                    Loader.modLogger?.LogInfo("[Conquest-UI] Conquest mode selected (TRUE).");
                }
                else
                if (selectedText.Equals("Reign", StringComparison.OrdinalIgnoreCase))
                {
                    IsConquestSelected = false;
                    IsReignSelected = true;
                    __instance.view.SetShowGameModeDescriptionText("gamemode.reign.description");
                    Loader.modLogger?.LogInfo($"[Conquest-UI] Reign mode selected (True).");
                }
                else
                {
                    IsConquestSelected = false;
                    IsReignSelected = false;
                    Loader.modLogger?.LogInfo($"[Conquest-UI] Mode changed to: {selectedText} (FALSE).");
                }

                if (GameManager.PreliminaryGameSettings.GameType == GameType.SinglePlayer)
                {
                    CreateOpponentsList(__instance);
                }
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
                if (GameManager.PreliminaryGameSettings.GameType != GameType.SinglePlayer)
                {
                    return;
                }

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

                if (mapPreset == (MapPreset)5)
                {
                    if (mapSize <= 16)
                    {
                        __result = 1;
                    }
                    else
                    if (mapSize <= 18)
                    {
                        __result = 2;
                    }
                    else
                    if (mapSize <= 20)
                    {
                        __result = 3;
                    }
                    else
                    {
                        // Massive (30) 
                        __result = 7;
                    }
                }
                else
                if (mapPreset == (MapPreset)6)
                {
                    if (mapSize <= 20) // All except Massive (30)
                    {
                        __result = 3;
                    }
                    else
                    {
                        // Massive (30) 
                        __result = 7;
                    }
                }
                else
                {
                    if (mapSize <= 16) // Tiny (11) & Small (14) & Normal (16)
                    {
                        __result = 3;
                    }
                    else
                    if (mapSize <= 18) // Large (18)
                    {
                        __result = 4;
                    }
                    else
                    if (mapSize <= 20) // Huge (20)
                    {
                        __result = 5;
                    }
                    else
                    {
                        // Massive (30) 
                        __result = 7;
                    }
                }

                Loader.modLogger?.LogInfo($"[Conquest-Backend] MapSize {mapSize} set. MapType {mapPreset} detected. Limit set to {__result}.");
                return false;

            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Backend] MapDataExtensions error: {ex}");
            }
            return true;
        } 
        
        // =========================================================================
        // B. Game Stats Screen
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameStatsScreen), nameof(GameStatsScreen.Show))]
        public static void Show_Reign(GameStatsScreen __instance, bool instant = false)
        {
            try
            {
                if (__instance.GameSettings.RulesGameMode == EnumCache<GameMode>.GetType("reign"))
                {
                    __instance.scoreHeader.Key = "gamestatus.capitals";
                }
                __instance.PopulateScreen();
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Backend] GameStatsScreen Show error: {ex}");
            } 
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameStatsScreen), nameof(GameStatsScreen.PopulateScreen))]
        public static void PopulateScreen_Conquest(GameStatsScreen __instance)
        {
            try
            {
                if (__instance.GameSettings.RulesGameMode != EnumCache<GameMode>.GetType("conquest"))
                {
                    return;
                }

                __instance.ClearStatsRows();
                __instance.moreInfoButton.SetData(__instance.PrepareGameInfo());
                __instance.PopulateStatsList();
                __instance.PopulatePlayers();
                __instance.PopulateTasks();
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Backend] GameStatsScreen PopulateScreen error: {ex}");
            } 
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameStatsScreen), nameof(GameStatsScreen.GetDescription))]
        public static void GetDescription_Reign(
            GameStatsScreen __instance,
            PlayerState player,
            int cityCount,
            string userName,
            ref string __result)
        {
            try
            {
                if (__instance == null)
                    return;

                // Settings / mode
                GameSettings? settings = null;
                try { settings = __instance.GameSettings; } catch { }
                if (settings == null)
                    return;

                if (settings.RulesGameMode != EnumCache<GameMode>.GetType("reign")
                    && settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest"))
                    return;

                if (player == null)
                {
                    __result = string.IsNullOrEmpty(userName) ? "Player" : userName;
                    return;
                }

                GameState gameState = GameManager.GameState;
                PlayerState? localPlayer = null;
                try { localPlayer = GameManager.LocalPlayer; } catch { }

                bool isSpectating = false;
                try
                {
                    if (GameManager.Client != null)
                        isSpectating = GameManager.Client.IsSpectating;
                }
                catch { }

                bool isLocalHuman = localPlayer != null
                    && player == localPlayer
                    && !isSpectating;

                bool autoPlay = false;
                try { autoPlay = player.AutoPlay; } catch { }

                bool isAlive = true;
                try
                {
                    if (gameState != null)
                        isAlive = player.IsAlive(gameState);
                }
                catch { isAlive = true; }

                bool knowsPlayer = false;
                try
                {
                    if (localPlayer != null)
                        knowsPlayer = localPlayer.KnowsPlayer(player.Id);
                }
                catch { }

                bool showName =
                    isLocalHuman
                    || knowsPlayer
                    || !isAlive
                    || (isSpectating && localPlayer != null && localPlayer.Id == player.Id)
                    || !autoPlay;

                string safeName = string.IsNullOrEmpty(userName)
                    ? ("Player " + player.Id)
                    : userName;

                string arg;
                try
                {
                    if (showName)
                    {
                        arg = Localization.Get(
                            "gamestatus.ruled",
                            new Il2CppReferenceArray<Il2CppSystem.Object>(new Il2CppSystem.Object[]
                            {
                                (Il2CppSystem.Object)(Il2CppSystem.String)safeName
                            }));
                    }
                    else
                    {
                        arg = Localization.Get(
                            "gamestatus.unknown.ruler",
                            new Il2CppReferenceArray<Il2CppSystem.Object>(0));
                    }
                }
                catch
                {
                    arg = showName ? safeName : "???";
                }

                if (string.IsNullOrEmpty(arg))
                    arg = showName ? safeName : "???";

                // Dead: name/unknown only
                if (!isAlive)
                {
                    __result = arg;
                    return;
                }

                // Alive: name + score
                string scorePart;
                try
                {
                    string scoreStr = LocalizationUtils.FormatNumber(player.score);
                    scorePart = Localization.Get(
                        "gamestatus.score",
                        new Il2CppReferenceArray<Il2CppSystem.Object>(new Il2CppSystem.Object[]
                        {
                            (Il2CppSystem.Object)(Il2CppSystem.String)scoreStr
                        }));
                }
                catch
                {
                    scorePart = player.score.ToString();
                }

                if (string.IsNullOrEmpty(scorePart))
                    scorePart = player.score.ToString();

                __result = string.Format("{0}, {1}", arg, scorePart);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Backend] GameStatsScreen GetDescription error: {ex}");
                try
                {
                    __result = string.IsNullOrEmpty(userName) ? "Player" : userName;
                }
                catch
                {
                    __result = "Player";
                }
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GameStatsScreen), nameof(GameStatsScreen.PopulatePlayers))]
        public static bool PopulatePlayers_Reign(GameStatsScreen __instance)
        {
            try
            {
                if (__instance.GameSettings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return true;
                }

                Il2CppSystem.Collections.Generic.List<PlayerState> playersSortedByRank = GameManager.GameState.GetPlayersSortedByRank();
                PlayerState localPlayer = GameManager.LocalPlayer;

                foreach (PlayerState player in playersSortedByRank)
                {
                    player.opinions.UpdateOpinions(GameManager.GameState, player);
                    if (player.Id != 255)
                    {
                        bool flag = player == localPlayer;
                        bool autoPlay = player.AutoPlay;
                        bool flag2 = player.IsAlive(GameManager.GameState);
                        bool flag3 = flag || GameManager.LocalPlayer.KnowsPlayer(player.Id) || !flag2;

                        StatsRowView row = __instance.CreateStatsRow(flag2 ? __instance.scoresList : __instance.deadScoresList);
                        string statsName = string.Empty;

                        // 1. 如果 player.AccountId 本身就不是 null，我們可以直接拿來用
                        Il2CppSystem.Nullable<Il2CppSystem.Guid> friendIdParam;

                        if (player.AccountId != null && player.AccountId.HasValue)
                        {
                            friendIdParam = player.AccountId;
                        }
                        else
                        {
                            // 2. 如果是 null，建立一個包含 Il2CppSystem.Guid.Empty 的新 Nullable 物件
                            // 這是最安全的做法，完全避開與 .NET Guid 的型別混淆
                            Il2CppSystem.Guid emptyGuid = default; 
                            friendIdParam = new Il2CppSystem.Nullable<Il2CppSystem.Guid>(emptyGuid);
                        }

                        // 3. 成功傳入 FriendUtils
                        string spriteStringForFriendIdWithFormat = FriendUtils.GetSpriteStringForFriendIdWithFormat(
                            friendIdParam, 
                            " <size=80%>{0}</size>"
                        );
                        PlayerData playerData;
                        GameManager.Client.TryGetPlayerData(player.Id, out playerData);
                        string userName = autoPlay ? string.Format("{0} ({1})", spriteStringForFriendIdWithFormat + playerData.GetPresentableName(25), Localization.Get("gamestatus.ruled.bot", new Il2CppReferenceArray<Il2CppSystem.Object>(0))) : (spriteStringForFriendIdWithFormat + playerData.GetPresentableName(25));

                        /*row.button.enabled = true;
                        row.OnClicked += delegate(int index, BaseEventData eventData)
                        {
                            PlayerInfoPopup playerInfoPopup = PopupManager.GetPlayerInfoPopup();
                            playerInfoPopup.SetData(player);
                            playerInfoPopup.Show(InputManager.GetInputPosition());
                        };*/
                        
                        PlayerInfoIcon.LayoutInfo layoutInfo = new PlayerInfoIcon.LayoutInfo();
                        layoutInfo.tribe = player.tribe;
                        layoutInfo.skin = player.skinType;
                        layoutInfo.mood = PlayerInfoIcon.Mood.DataBased;
                        TribeData tribeData;
                        GameManager.GameState.GameLogicData.TryGetData(player.tribe, out tribeData);
                        layoutInfo.color = ColorUtil.ColorFromInt((int)tribeData.color);
                        DiplomacyRelation relation = GameManager.LocalPlayer.GetRelation(player.Id);
                        layoutInfo.diplomacyState = relation.State;

                        if (flag3)
                        {
                            row.iconContainerPlayerInfoIcon.gameObject.SetActive(true);
                            row.iconContainerPlayerInfoIcon.SetData(player, GameManager.LocalPlayer);

                            Image head = row.iconContainerPlayerInfoIcon.HeadImage;
                            if (head != null)
                            {
                                head.preserveAspect = true;
                                RectTransform headRt = head.rectTransform;
                                headRt.anchorMin = Vector2.zero;
                                headRt.anchorMax = Vector2.one;
                                headRt.pivot = new Vector2(0.5f, 0.5f);
                                headRt.anchoredPosition = Vector2.zero;
                                headRt.sizeDelta = Vector2.zero;
                            }

                            row.OnClickedSignal.Add((System.Action)(() => {
                                PlayerInfoPopup playerInfoPopup = PopupManager.GetPlayerInfoPopup();
                                playerInfoPopup.SetData(player);
                                playerInfoPopup.Show(InputManager.GetInputPosition());
                            }));

                            row.SetShowIconType(StatsRowView.IconType.PlayerInfo);
                            row.SetPlayerInfoIconLayout(layoutInfo);
                            statsName = player.GetLocalizedTribeName(GameManager.GameState);
                        }
                        else
                        {
                            statsName = Localization.Get("gamestatus.unknown.tribe", new Il2CppReferenceArray<Il2CppSystem.Object>(0));
                        }

                        string statsValue = LocalizationUtils.FormatNumber(player.score);
                        int num = 0;
                        if (__instance.GameSettings.RulesGameMode == EnumCache<GameMode>.GetType("reign"))
                        {
                            foreach (PlayerState player2 in playersSortedByRank)
                            {
                                if (player2.Id != 255)
                                {
                                    num++;
                                }
                            }
                        }
                        int num2 = 0;
                        if (flag2)
                        {

                                num2 = player.CountCapitals(GameManager.GameState);
                                statsValue = string.Format("{0}/{1}", num2, 1);

                        }
                        int totalIncomeFromEmbassiesAndDividend = localPlayer.GetTotalIncomeFromEmbassiesAndDividend(player, GameManager.GameState);
                        row.SetEmbassyIncome(totalIncomeFromEmbassiesAndDividend);
                        string description = __instance.GetDescription(player, num2, userName);
                        // string description2 = __instance.FitDescription(player, row, num2, userName);
                        // Loader.modLogger?.LogInfo($"Sublabel: {description}");
                        // Loader.modLogger?.LogInfo($"Sublabel: {description2}");
                        row.SetLabelAndValue(statsName, statsValue);
                        row.SetSubLabel(description);
                        row.SetSmallValue($"{playerData.profile.multiplayerRating}");

                        row.SetShowSubLabel(true);
                        if (playerData.profile.multiplayerRating != 0)
                        {
                            row.SetShowSmallValue(true);
                            row.SetShowEloButton(flag3);
                        }
                        row.SetShowHighlightRow(flag);
                        row.SetShowEmbassyContainer(totalIncomeFromEmbassiesAndDividend > 0);
                        row.RunLayoutInternal();
                        row.SetActive(true);
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Backend] GameStatsScreen PopulatePlayers error: {ex}");
                return true;
            } 
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameState), nameof(GameState.GetPlayersSortedForGameMode))]
        public static void GetPlayersSortedForGameMode_Conquest(Il2CppSystem.Collections.Generic.List<PlayerState> players, GameMode gameMode, bool shouldIgnoreResigns, ref Il2CppSystem.Collections.Generic.List<PlayerState>  __result)
        {
            try
            {
                if (gameMode != EnumCache<GameMode>.GetType("conquest"))
                {
                    return;
                }

                players = GameState.GetPlayersSortedByCities(players);

                __result = GameState.GetPlayersSortedByElimination(players, shouldIgnoreResigns);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Backend] GetPlayersSortedForGameMode error: {ex}");
            } 
        }
        
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameModeButtonWrapper), nameof(GameModeButtonWrapper.SetData))]
        public static void SetData_GamemodeInfo(GameModeButtonWrapper __instance, GameMode summaryGameMode, GameType gameType, int scoreLimit = 10000)
        {
            try
            {
                if (summaryGameMode != EnumCache<GameMode>.GetType("conquest")
                    && summaryGameMode != EnumCache<GameMode>.GetType("reign"))
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
        public static bool OnButtonClicked_GamemodeInfo(GameModeButtonWrapper __instance, int id, UnityEngine.EventSystems.BaseEventData? eventData = null)
        {
            try
            {
                if (__instance.currentGameMode != EnumCache<GameMode>.GetType("conquest")
                    && __instance.currentGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return true;
                }
                
                string modeName = __instance.currentGameMode.GetName();
                string HeaderText = LocalizationUtils.CapitalizeString(modeName);

              	BasicPopup basicPopup = PopupManager.GetBasicPopup();
                basicPopup.Header = HeaderText;

                string? text = null;
                string? text2 = Localization.Get(GameModeUtils.GetDescription(__instance.currentGameMode), (Il2CppReferenceArray<Il2CppSystem.Object>)Array.Empty<Il2CppSystem.Object>());

                if (__instance.currentGameMode == EnumCache<GameMode>.GetType("conquest"))
                {
                    text = text2;
                } 
                else if (__instance.currentGameMode == EnumCache<GameMode>.GetType("reign"))
                {

                    text = $"Game mode: Reign\n{text2}";
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

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameModeUtils), nameof(GameModeUtils.GetDescription))]
        public static void GetDescription_GamemodeInfo(GameMode gameMode, ref string __result)
        {
            try
            {
                if (gameMode == EnumCache<GameMode>.GetType("conquest"))
                {
                    __result = "gamemode.conquest.description";
                }
                else if (gameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    __result = "gamemode.reign.description";
                }
                else
                {
                    __result = string.Empty;
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Backend] GameModeUtils error: {ex}");
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