using HarmonyLib;
using PolytopiaBackendBase.Game;
using UnityEngine;
using UnityEngine.UI;
using Polytopia.Data;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime;
using PolyMode;
using Il2CppSystem.Reflection;
using System.Reflection;
using BindingFlags = System.Reflection.BindingFlags;
using UnityEngine.UIElements.UIR;

namespace Rush
{
    public static class UI_2
    {
        // =========================================================================
        // A. Game Setup Screen
        // =========================================================================
        public static int RushTurnLimit = 0;

        // 0 = not Rush, 1 = Star (rusha), 2 = Army (rushb), 3 = Tech (rushc)
        // SP: sticky until MapGenerator.Generate
        // Multi: sticky until BackendAdapter.CreateLobby
        static int _rushGoalId = 0;

        public static int RushGoalId => _rushGoalId;

        static UIHorizontalList_UI2? goalList;
        static UIHorizontalListData? goalListData;
        static TextField_UI2? goalDescText;
        static UIHorizontalList_UI2? turnLimitList;
        static UIHorizontalListData? turnLimitListData;

        static float _ui2SavedNorm = 1f;
        static bool _ui2HasSaved;

        static bool IsRushMode()
        {
            var s = GameManager.PreliminaryGameSettings;
            if (s == null) return false;
            var ra = EnumCache<GameMode>.GetType("rusha");
            var rb = EnumCache<GameMode>.GetType("rushb");
            var rc = EnumCache<GameMode>.GetType("rushc");
            return s.RulesGameMode == ra || s.RulesGameMode == rb || s.RulesGameMode == rc
                || s.BaseGameMode == ra || s.BaseGameMode == rb || s.BaseGameMode == rc;
        }

        /// <summary>
        /// Setup always keeps mode list valid: Rules/Base = rusha.
        /// Sticky 1/2/3; SP commits in Generate, multi in CreateLobby.
        /// </summary>
        static void ApplyRushGoalToSettings(int goalId)
        {
            if (goalId < 1 || goalId > 3) return;
            _rushGoalId = goalId;

            var ra = EnumCache<GameMode>.GetType("rusha");
            var s = GameManager.PreliminaryGameSettings;

            if (s.GameType == GameType.SinglePlayer)
                s.RulesGameMode = ra;
            else
                s.BaseGameMode = ra; // never rushb/rushc in setup list id

            s.SaveToDisk();
        }

        public static GameMode GetCommittedRushMode()
        {
            var ra = EnumCache<GameMode>.GetType("rusha");
            var rb = EnumCache<GameMode>.GetType("rushb");
            var rc = EnumCache<GameMode>.GetType("rushc");
            if (RushGoalId == 3) return rc;
            if (RushGoalId == 2) return rb;
            return ra;
        }

        static bool IsAlive(UIHorizontalList_UI2? list)
        {
            try { return list != null && list.gameObject != null; }
            catch { return false; }
        }

        static void ClearDeadListRefs()
        {
            if (!IsAlive(goalList)) goalList = null;
            if (!IsAlive(turnLimitList)) turnLimitList = null;
            try
            {
                if (goalDescText == null || goalDescText.gameObject == null)
                    goalDescText = null;
            }
            catch { goalDescText = null; }
        }

        static void HideRushLists()
        {
            ClearDeadListRefs();
            if (goalList != null)
            {
                try { goalList.KillScrollTween(); } catch { }
                try { goalList.ActiveSelf = false; } catch { }
            }
            if (goalDescText != null)
            {
                try { goalDescText.ActiveSelf = false; } catch { }
            }
            if (turnLimitList != null)
            {
                try { turnLimitList.KillScrollTween(); } catch { }
                try { turnLimitList.ActiveSelf = false; } catch { }
            }
        }

        static void EnsureRushLists(GameSetupScreenView view)
        {
            if (view?.scroller?.content == null) return;
            ClearDeadListRefs();

            if (goalList == null)
            {
                goalList = UILibrary.NewHorizontalList(view.scroller.content);
                goalList.gameObject.name = "goalList";
                goalList.ActiveSelf = false;
                goalList.OnItemSelected().Add(
                    DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int>>(OnGoalChanged));
            }

            if (goalDescText == null)
            {
                goalDescText = UILibrary.NewText(view.scroller.content, "goalDescText");
                goalDescText.gameObject.name = "goalDescText";
                goalDescText.ActiveSelf = false;
            }

            if (turnLimitList == null)
            {
                turnLimitList = UILibrary.NewHorizontalList(view.scroller.content);
                turnLimitList.gameObject.name = "turnLimitList";
                turnLimitList.ActiveSelf = false;
                turnLimitList.OnItemSelected().Add(
                    DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int>>(OnTurnLimitChanged));
            }
        }

        static bool LabelsContainRush(Il2CppSystem.Collections.Generic.List<string> labels)
        {
            if (labels == null) return false;
            for (int i = 0; i < labels.Count; i++)
            {
                var t = labels[i];
                if (t == null) continue;
                if (t.Equals("Rush", StringComparison.OrdinalIgnoreCase)) return true;
                try
                {
                    if (t == Localization.Get("gamemode.rusha")) return true;
                    if (t == Localization.Get("gamemode.rushb")) return true;
                    if (t == Localization.Get("gamemode.rushc")) return true;
                }
                catch { }
            }
            return false;
        }

        static void PushContentBelow(GameSetupScreenView view, float yThreshold, float amount)
        {
            if (view?.scroller?.content == null || amount <= 0f) return;

            RectTransform content = view.scroller.content;
            for (int i = 0; i < content.childCount; i++)
            {
                var rt = content.GetChild(i) as RectTransform;
                if (rt == null || !rt.gameObject.activeInHierarchy) continue;

                string n = rt.gameObject.name ?? "";
                if (n == "goalList" || n == "turnLimitList" || n == "goalDescText") continue;

                if (rt.anchoredPosition.y <= yThreshold)
                {
                    var p = rt.anchoredPosition;
                    p.y -= amount;
                    rt.anchoredPosition = p;
                }
            }

            if (view.allComponents != null)
            {
                IntPtr goalPtr = IsAlive(goalList) ? goalList.Pointer : IntPtr.Zero;
                IntPtr turnPtr = IsAlive(turnLimitList) ? turnLimitList.Pointer : IntPtr.Zero;

                foreach (var c in view.allComponents)
                {
                    if (c == null || !c.ActiveSelf) continue;
                    try
                    {
                        if (goalPtr != IntPtr.Zero && c.Pointer == goalPtr) continue;
                        if (turnPtr != IntPtr.Zero && c.Pointer == turnPtr) continue;
                        if (c.GetY() <= yThreshold)
                            c.SetY(c.GetY() - amount - 10f);
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// Mode list has a single "Rush" row (id rusha).
        /// If BaseGameMode is rusha/rushb/rushc, keep that row selected.
        /// </summary>
        static void SelectRushRowInModeList(GameSetupScreen_UI2? screen)
        {
            if (screen?.gameModeData?.labels == null) return;
            if (!IsRushMode() && _rushGoalId < 1) return;

            var labels = screen.gameModeData.labels;
            for (int i = 0; i < labels.Count; i++)
            {
                var t = labels[i]?.ToString() ?? "";
                if (!t.Equals("Rush", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    if (screen.view?.listGameMode != null)
                        screen.view.listGameMode.SelectedIndex = i;
                }
                catch { }
                break;
            }
        }

        static void PlaceRushLists(GameSetupScreenView view, ScreenBase_UI2.ScreenSize screenSize)
        {
            if (view == null) return;
            if (!IsAlive(goalList)) return;
            if (goalListData == null) return;

            var s = GameManager.PreliminaryGameSettings;
            var ra = EnumCache<GameMode>.GetType("rusha");
            var rb = EnumCache<GameMode>.GetType("rushb");
            var rc = EnumCache<GameMode>.GetType("rushc");

            int goalIndex = 0;
            if (_rushGoalId == 3) goalIndex = 2;
            else if (_rushGoalId == 2) goalIndex = 1;
            else if (_rushGoalId == 1) goalIndex = 0;
            else if (s.GameType == GameType.SinglePlayer)
            {
                if (s.RulesGameMode == rc) { goalIndex = 2; _rushGoalId = 3; }
                else if (s.RulesGameMode == rb) { goalIndex = 1; _rushGoalId = 2; }
                else if (s.RulesGameMode == ra) { goalIndex = 0; _rushGoalId = 1; }
            }
            else
            {
                if (s.BaseGameMode == rc) { goalIndex = 2; _rushGoalId = 3; }
                else if (s.BaseGameMode == rb) { goalIndex = 1; _rushGoalId = 2; }
                else if (s.BaseGameMode == ra) { goalIndex = 0; _rushGoalId = 1; }
            }

            int goalId = (_rushGoalId >= 1 && _rushGoalId <= 3)
                ? _rushGoalId
                : (goalIndex == 2 ? 3 : goalIndex == 1 ? 2 : 1);

            // ----- Goal list -----
            goalList.SetData(goalListData.header, goalListData.GetLabels());
            goalList.SetWidth(screenSize.screenRect.Width);
            goalList.UpdateLayout();
            goalList.SelectedIndex = goalIndex;
            goalList.ActiveSelf = true;

            float goalH = goalList.GetHeight();
            if (goalH < 1f) goalH = 56f;

            // ----- Goal description -----
            const float padDesc = 10f;
            const float minDescH = 40f;
            float descH = 0f;

            if (goalDescText != null)
            {
                RefreshGoalDescription(goalId);
                goalDescText.ActiveSelf = true;
                try { goalDescText.SetWidth(screenSize.screenRect.Width); } catch { }

                try
                {
                    var rt = goalDescText.transform as RectTransform;
                    if (rt != null)
                        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
                }
                catch { }

                try { descH = goalDescText.GetHeight(); } catch { descH = 0f; }
                if (descH < minDescH) descH = minDescH;
            }

            // ----- Turn limit (multi only) -----
            bool showTurn =
                s.GameType != GameType.SinglePlayer
                && IsAlive(turnLimitList)
                && turnLimitListData != null;

            float turnH = 0f;
            if (showTurn)
            {
                int turnIndex = 1;
                int limit = s.rules.ScoreLimit;
                if (limit == 20) turnIndex = 0;
                else if (limit == 30) turnIndex = 1;
                else if (limit == 50) turnIndex = 2;

                turnLimitList.SetData(turnLimitListData.header, turnLimitListData.GetLabels());
                turnLimitList.SetWidth(screenSize.screenRect.Width);
                turnLimitList.UpdateLayout();
                turnLimitList.SelectedIndex = turnIndex;
                turnLimitList.ActiveSelf = true;

                turnH = turnLimitList.GetHeight();
                if (turnH < 1f) turnH = 56f;
            }
            else if (IsAlive(turnLimitList))
            {
                turnLimitList.ActiveSelf = false;
            }

            // ----- Anchor -----
            UIBasicComponent? goalAnchor = null;
            bool underDescription = false;
            if (view.gameModeDescriptionText != null && view.gameModeDescriptionText)
            {
                goalAnchor = view.gameModeDescriptionText;
                underDescription = true;
            }
            else if (view.listGameMode != null && view.listGameMode.ActiveSelf)
            {
                goalAnchor = view.listGameMode;
            }

            if (goalAnchor == null)
            {
                goalList.ActiveSelf = false;
                if (goalDescText != null)
                {
                    try { goalDescText.ActiveSelf = false; } catch { }
                }
                if (IsAlive(turnLimitList)) turnLimitList.ActiveSelf = false;
                return;
            }

            float padGoal = underDescription ? -3f : 10f;
            float padTurn = showTurn ? 10f : 0f;

            float totalExtra = goalH
                + (showTurn ? turnH + padTurn : 0f)
                + (goalDescText != null ? descH + padDesc : 0f)
                + Mathf.Max(padGoal, 0f);

            PushContentBelow(view, goalAnchor.GetY() - 0.5f, totalExtra + 10f);

            float goalY = goalAnchor.GetY() - goalH - padGoal;
            goalList.SetPosition(goalAnchor.GetX(), goalY);
            goalList.UpdateLayout();

            float cursorY = goalList.GetY() - goalH;

            if (showTurn)
            {
                float turnY = cursorY - padTurn;
                turnLimitList.SetPosition(goalList.GetX(), turnY);
                turnLimitList.UpdateLayout();
                cursorY = turnY - turnH;
            }

            if (goalDescText != null && goalDescText.ActiveSelf)
            {
                float descY = cursorY - padDesc * 2 + descH;
                goalDescText.SetPosition(goalList.GetX(), descY);
            }

            if (IsRushMode() && _rushGoalId >= 1 && _rushGoalId <= 3)
                ApplyRushGoalToSettings(_rushGoalId);

            FixSetupScroller(view, jumpToTop: false);
        }

        static string GetGoalDescription(int goalId)
        {
            try
            {
                if (goalId == 3)
                    return Localization.Get("gamemode.goal.rushc.description");
                if (goalId == 2)
                    return Localization.Get("gamemode.goal.rushb.description");
                return Localization.Get("gamemode.goal.rusha.description");
            }
            catch
            {
                if (goalId == 3)
                    return "Race for knowledge. Monuments research if the city has no units.";
                if (goalId == 2)
                    return "Win by army strength (units + kills).";
                return "Win by stars (currency).";
            }
        }

        static void RefreshGoalDescription(int goalId)
        {
            if (goalDescText == null) return;
            try
            {
                string text = GetGoalDescription(goalId);
                goalDescText.Text = text;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning($"[Rush-UI] goalDesc: {ex.Message}");
            }
        }

        static void FixSetupScroller(GameSetupScreenView view, bool jumpToTop = false)
        {
            try
            {
                if (view?.scroller?.content == null) return;
                RectTransform content = view.scroller.content;

                float minBottom = 0f;

                for (int i = 0; i < content.childCount; i++)
                {
                    var rt = content.GetChild(i) as RectTransform;
                    if (rt == null || !rt.gameObject.activeInHierarchy) continue;

                    float h = Mathf.Abs(rt.rect.height);
                    if (h < 1f)
                    {
                        try { h = LayoutUtility.GetPreferredHeight(rt); } catch { h = 40f; }
                    }
                    float bottom = rt.anchoredPosition.y - h;
                    if (bottom < minBottom) minBottom = bottom;
                }

                if (view.allComponents != null)
                {
                    foreach (var c in view.allComponents)
                    {
                        if (c == null || !c.ActiveSelf) continue;
                        try
                        {
                            float bottom = c.GetY() - c.GetHeight();
                            if (bottom < minBottom) minBottom = bottom;
                        }
                        catch { }
                    }
                }

                if (IsAlive(goalList))
                {
                    try
                    {
                        float b = goalList.GetY() - goalList.GetHeight();
                        if (b < minBottom) minBottom = b;
                    }
                    catch { }
                }
                if (IsAlive(turnLimitList) && turnLimitList.ActiveSelf)
                {
                    try
                    {
                        float b = turnLimitList.GetY() - turnLimitList.GetHeight();
                        if (b < minBottom) minBottom = b;
                    }
                    catch { }
                }

                float needH = -minBottom + 200f;

                Vector2 size = content.sizeDelta;
                if (needH > size.y)
                {
                    size.y = needH;
                    content.sizeDelta = size;
                }

                try
                {
                    Vector2 offMin = content.offsetMin;
                    if (offMin.y > minBottom - 200f)
                    {
                        offMin.y = minBottom - 200f;
                        content.offsetMin = offMin;
                    }
                }
                catch { }

                ScrollRect sr = view.scroller.GetComponent<ScrollRect>()
                    ?? view.scroller.GetComponentInChildren<ScrollRect>(true);

                if (sr != null)
                {
                    sr.horizontal = false;
                    sr.vertical = true;
                    sr.movementType = ScrollRect.MovementType.Clamped;
                    sr.inertia = true;
                    sr.scrollSensitivity = 60f;

                    LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                    Canvas.ForceUpdateCanvases();

                    if (jumpToTop)
                    {
                        sr.verticalNormalizedPosition = 1f;
                        _ui2SavedNorm = 1f;
                        _ui2HasSaved = true;
                    }
                    else
                    {
                        float n = _ui2HasSaved ? _ui2SavedNorm : sr.verticalNormalizedPosition;
                        sr.verticalNormalizedPosition = Mathf.Clamp01(n);
                    }
                }

                try { content.ForceUpdateRectTransforms(); } catch { }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning($"[Rush-UI] FixSetupScroller: {ex.Message}");
            }
        }

        // -------------------------------------------------------------------------
        // Harmony
        // -------------------------------------------------------------------------

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIHorizontalListData), nameof(UIHorizontalListData.AddItem))]
        public static void AddItem_Rush(UIHorizontalListData __instance, string label, int id)
        {
            if (__instance == null || label == null) return;
            try
            {
                var gt = GameManager.PreliminaryGameSettings.GameType;
                bool trigger =
                    (gt == GameType.SinglePlayer && label == Localization.Get("gamemode.conquest"))
                    || ((gt == GameType.Competitive || gt == GameType.Multiplayer
                        || gt == GameType.Matchmaking || gt == GameType.PassAndPlay)
                        && label == Localization.Get("gamemode.reign"));

                if (!trigger) return;
                if (LabelsContainRush(__instance.labels)) return;

                int Id = (int)EnumCache<GameMode>.GetType("rusha");
                __instance.AddItem("Rush", Id);
                Loader.modLogger?.LogInfo($"[Rush-UI] Added Rush id={Id}");
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Rush-UI] AddItem: {ex}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreen_UI2), nameof(GameSetupScreen_UI2.OnGameModeChanged))]
        public static void OnGameModeChanged_Rush(GameSetupScreen_UI2 __instance, int index)
        {
            if (__instance?.view == null || __instance.gameModeData?.labels == null) return;
            try
            {
                if (index < 0 || index >= __instance.gameModeData.labels.Count) return;
                string selectedText = __instance.gameModeData.labels[index]?.ToString() ?? "";

                if (selectedText.Equals("Rush", StringComparison.OrdinalIgnoreCase))
                {
                    if (RushTurnLimit <= 0) RushTurnLimit = 30;
                    if (GameManager.PreliminaryGameSettings.rules.ScoreLimit <= 0)
                        GameManager.PreliminaryGameSettings.rules.ScoreLimit = RushTurnLimit;

                    if (_rushGoalId < 1 || _rushGoalId > 3)
                        _rushGoalId = 1;

                    ApplyRushGoalToSettings(_rushGoalId);
                    Loader.modLogger?.LogInfo($"[Rush-UI] Rush selected goalId={_rushGoalId}");
                }
                else
                {
                    _rushGoalId = 0;
                    RushTurnLimit = 0;
                    Loader.modLogger?.LogInfo($"[Rush-UI] Mode: {selectedText}");
                }

                try { __instance.UpdateLayout(); }
                catch (Exception ex)
                {
                    Loader.modLogger?.LogWarning($"[Rush-UI] UpdateLayout: {ex.Message}");
                }

                if (IsRushMode())
                    SelectRushRowInModeList(__instance);

                if (GameManager.PreliminaryGameSettings.GameType == GameType.SinglePlayer)
                    CreateOpponentsList(__instance);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning($"[Rush-UI] OnGameModeChanged: {ex.Message}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreen_UI2), nameof(GameSetupScreen_UI2.OnShow))]
        public static void OnShow_InitLists(GameSetupScreen_UI2 __instance)
        {
            try
            {
                ClearDeadListRefs();

                var s = GameManager.PreliminaryGameSettings;
                if (s != null)
                {
                    var ra = EnumCache<GameMode>.GetType("rusha");
                    var rb = EnumCache<GameMode>.GetType("rushb");
                    var rc = EnumCache<GameMode>.GetType("rushc");
                    if (s.GameType == GameType.SinglePlayer)
                    {
                        if (s.RulesGameMode == rc) _rushGoalId = 3;
                        else if (s.RulesGameMode == rb) _rushGoalId = 2;
                        else if (s.RulesGameMode == ra) _rushGoalId = 1;
                    }
                    else
                    {
                        if (s.BaseGameMode == rc) _rushGoalId = 3;
                        else if (s.BaseGameMode == rb) _rushGoalId = 2;
                        else if (s.BaseGameMode == ra) _rushGoalId = 1;
                    }
                }

                try { __instance.UpdateLayout(); }
                catch (Exception ex)
                {
                    Loader.modLogger?.LogWarning($"[Rush-UI] OnShow UpdateLayout: {ex.Message}");
                }

                if (IsRushMode())
                    SelectRushRowInModeList(__instance);

                if (GameManager.PreliminaryGameSettings.GameType != GameType.SinglePlayer) return;
                if (!IsRushMode()) return;
                CreateOpponentsList(__instance);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning($"[Rush-UI] OnShow: {ex.Message}");
            }
        }

        private static void CreateOpponentsList(GameSetupScreen_UI2 instance)
        {
            if (instance?.view == null) return;

            int allowedMaxOpponents = MapDataExtensions.GetMaximumOpponentCountForMapSize(
                GameManager.PreliminaryGameSettings.MapSize,
                GameManager.PreliminaryGameSettings.mapPreset);

            if (allowedMaxOpponents <= 0 || allowedMaxOpponents > 15)
                allowedMaxOpponents = GameManager.GetMaxOpponents();

            var uiLabels = new Il2CppSystem.Collections.Generic.List<string>();
            for (int i = 0; i <= allowedMaxOpponents; i++)
                uiLabels.Add(i.ToString());

            instance.view.SetShowOpponents("Opponents", uiLabels, allowedMaxOpponents + 1);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapDataExtensions), nameof(MapDataExtensions.GetMaximumOpponentCountForMapSize))]
        public static void GetMaximumOpponentCount_Rush(int mapSize, MapPreset mapPreset, ref int __result)
        {
            try
            {
                if (!IsRushMode()) return;

                if (mapSize == 0)
                    __result = 15;

                __result = (int)Math.Pow(mapSize / 3.0, 2.0) - 1;
                if (__result < 1) __result = 1;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Rush-Backend] MapSize: {ex}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreenView), nameof(GameSetupScreenView.Init))]
        static void GameSetupScreenView_Init(GameSetupScreenView __instance, RectTransform holder)
        {
            ClearDeadListRefs();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreenView), nameof(GameSetupScreenView.RunLayout))]
        static void GameSetupScreenView_RunLayout(
            GameSetupScreenView __instance,
            ScreenBase_UI2.ScreenSize screenSize)
        {
            try
            {
                if (!IsRushMode())
                {
                    HideRushLists();
                    return;
                }

                EnsureRushLists(__instance);

                var gt = GameManager.PreliminaryGameSettings.GameType;
                if (gt == GameType.SinglePlayer)
                {
                    goalListData = new UIHorizontalListData(3, "Goal");
                    goalListData.AddItem("Star", 1);
                    goalListData.AddItem("Army", 2);
                    goalListData.AddItem("Tech", 3);

                    turnLimitListData = new UIHorizontalListData(3, "Turn Limit");
                    turnLimitListData.AddItem("30", 2);
                    if (GameManager.PreliminaryGameSettings.rules.ScoreLimit <= 0)
                        GameManager.PreliminaryGameSettings.rules.ScoreLimit = 30;
                }
                else if (gt == GameType.Matchmaking || gt == GameType.Multiplayer
                    || gt == GameType.PassAndPlay || gt == GameType.Competitive)
                {
                    goalListData = new UIHorizontalListData(3, "Goal");
                    goalListData.AddItem("Star", 1);
                    goalListData.AddItem("Army", 2);
                    goalListData.AddItem("Tech", 3);

                    turnLimitListData = new UIHorizontalListData(3, "Turn Limit");
                    turnLimitListData.AddItem("20", 1);
                    turnLimitListData.AddItem("30", 2);
                    turnLimitListData.AddItem("50", 3);
                    if (GameManager.PreliminaryGameSettings.rules.ScoreLimit <= 0)
                        GameManager.PreliminaryGameSettings.rules.ScoreLimit = 30;
                }
                else
                {
                    HideRushLists();
                    return;
                }

                PlaceRushLists(__instance, screenSize);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Rush-Setup] RunLayout: {ex}");
            }
        }

        static void OnGoalChanged(int index)
        {
            if (goalListData == null) return;
            int id = goalListData.GetId(index); // 1 Star, 2 Army, 3 Tech
            ApplyRushGoalToSettings(id);
            RefreshGoalDescription(id);
            Loader.modLogger?.LogInfo($"[Rush-Map] Goal sticky={_rushGoalId}");
        }

        static void OnTurnLimitChanged(int index)
        {
            if (turnLimitListData == null) return;
            int id = turnLimitListData.GetId(index);
            if (id == 1) RushTurnLimit = 20;
            else if (id == 2) RushTurnLimit = 30;
            else if (id == 3) RushTurnLimit = 50;
            else return;

            GameManager.PreliminaryGameSettings.rules.ScoreLimit = RushTurnLimit;
            GameManager.PreliminaryGameSettings.SaveToDisk();
            Loader.modLogger?.LogInfo($"[Rush-Map] TurnLimit={RushTurnLimit}");
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
                var mode = __instance.GameSettings.RulesGameMode;
                if (mode != EnumCache<GameMode>.GetType("rusha")
                    && mode != EnumCache<GameMode>.GetType("rushb")
                    && mode != EnumCache<GameMode>.GetType("rushc"))
                {
                    return;
                }

                __instance.ClearStatsRows();
                __instance.moreInfoButton.SetData(__instance.PrepareGameInfo());
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
        public static void GetDescription_Rush(
            GameStatsScreen __instance,
            PlayerState player,
            int cityCount,
            string userName,
            ref string __result)
        {
            try
            {
                bool isSpectating = GameManager.Client.IsSpectating;
                bool flag = player == GameManager.LocalPlayer && !isSpectating;
                bool autoPlay = player.AutoPlay;
                bool flag2 = player.IsAlive(GameManager.GameState);
                bool flag3 = flag
                    || GameManager.LocalPlayer.KnowsPlayer(player.Id)
                    || !flag2
                    || (isSpectating && GameManager.LocalPlayer.Id == player.Id)
                    || !autoPlay;
                string arg = string.Empty;

                var mode = __instance.GameSettings.RulesGameMode;

                if (mode == EnumCache<GameMode>.GetType("rusha"))
                {
                    int starCount = player.Currency;

                    if (flag3)
                        arg = Localization.Get("gamestatus.ruled",
                            new Il2CppReferenceArray<Il2CppSystem.Object>(new[] { (Il2CppSystem.Object)userName }));
                    else
                        arg = Localization.Get("gamestatus.unknown.ruler",
                            new Il2CppReferenceArray<Il2CppSystem.Object>(0));

                    if (!flag2)
                    {
                        __result = string.Format("{0}", arg);
                        return;
                    }
                    __result = string.Format("{0}, {1}", arg,
                        Localization.Get((starCount > 1) ? "gamestatus.stars" : "gamestatus.star",
                            (Il2CppReferenceArray<Il2CppSystem.Object>)(new[] { (Il2CppSystem.Object)starCount })));
                }
                else if (mode == EnumCache<GameMode>.GetType("rushb"))
                {
                    int unitCount = player.CountUnits(GameManager.GameState);
                    int killCount = (int)player.kills;

                    if (flag3)
                        arg = Localization.Get("gamestatus.ruled",
                            new Il2CppReferenceArray<Il2CppSystem.Object>(new[] { (Il2CppSystem.Object)userName }));
                    else
                        arg = Localization.Get("gamestatus.unknown.ruler",
                            new Il2CppReferenceArray<Il2CppSystem.Object>(0));

                    if (!flag2)
                    {
                        __result = string.Format("{0}", arg);
                        return;
                    }
                    __result = string.Format("{0}, {1}, {2}", arg,
                        Localization.Get((unitCount > 1) ? "gamestatus.units" : "gamestatus.unit",
                            (Il2CppReferenceArray<Il2CppSystem.Object>)(new[] { (Il2CppSystem.Object)unitCount })),
                        Localization.Get((killCount > 1) ? "gamestatus.kills" : "gamestatus.kill",
                            (Il2CppReferenceArray<Il2CppSystem.Object>)(new[] { (Il2CppSystem.Object)killCount })));
                }
                else if (mode == EnumCache<GameMode>.GetType("rushc"))
                {
                    int techCount = AI_2.TechCount(player) - 1;

                    if (flag3)
                        arg = Localization.Get("gamestatus.ruled",
                            new Il2CppReferenceArray<Il2CppSystem.Object>(new[] { (Il2CppSystem.Object)userName }));
                    else
                        arg = Localization.Get("gamestatus.unknown.ruler",
                            new Il2CppReferenceArray<Il2CppSystem.Object>(0));

                    if (!flag2)
                    {
                        __result = string.Format("{0}", arg);
                        return;
                    }
                    __result = string.Format("{0}, {1}", arg,
                        Localization.Get((techCount > 1) ? "gamestatus.techs" : "gamestatus.tech",
                            (Il2CppReferenceArray<Il2CppSystem.Object>)(new[] { (Il2CppSystem.Object)techCount })));
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Rush-Backend] GameStatsScreen GetDescription error: {ex}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameState), nameof(GameState.GetPlayersSortedForGameMode))]
        public static void GetPlayersSortedForGameMode_Rush(
            Il2CppSystem.Collections.Generic.List<PlayerState>? players,
            GameMode gameMode,
            bool shouldIgnoreResigns,
            ref Il2CppSystem.Collections.Generic.List<PlayerState> __result)
        {
            try
            {
                if (gameMode == EnumCache<GameMode>.GetType("rusha"))
                    players = GetPlayersSortedByrusha(players);
                if (gameMode == EnumCache<GameMode>.GetType("rushb"))
                    players = GetPlayersSortedByrushb(players);
                if (gameMode == EnumCache<GameMode>.GetType("rushc"))
                    players = GetPlayersSortedByrushc(players);

                __result = GameState.GetPlayersSortedByElimination(players, shouldIgnoreResigns);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Rush-Backend] GetPlayersSortedForGameMode error: {ex}");
            }
        }

        public static Il2CppSystem.Collections.Generic.List<PlayerState>? GetPlayersSortedByrusha(
            Il2CppSystem.Collections.Generic.List<PlayerState>? players)
        {
            if (players == null || players.Count <= 1) return players;

            int count = players.Count;
            for (int i = 0; i < count - 1; i++)
            {
                int maxIndex = i;
                for (int j = i + 1; j < count; j++)
                {
                    PlayerState candidate = players[j];
                    PlayerState currentMax = players[maxIndex];

                    if (candidate.currency > currentMax.currency)
                        maxIndex = j;
                    else if (candidate.currency == currentMax.currency
                        && candidate.score > currentMax.score)
                        maxIndex = j;
                }

                if (maxIndex != i)
                {
                    PlayerState temp = players[i];
                    players[i] = players[maxIndex];
                    players[maxIndex] = temp;
                }
            }
            return players;
        }

        public static Il2CppSystem.Collections.Generic.List<PlayerState>? GetPlayersSortedByrushb(
            Il2CppSystem.Collections.Generic.List<PlayerState>? players)
        {
            if (players == null || players.Count <= 1) return players;

            int count = players.Count;
            for (int i = 0; i < count - 1; i++)
            {
                int maxIndex = i;
                for (int j = i + 1; j < count; j++)
                {
                    PlayerState candidate = players[j];
                    PlayerState currentMax = players[maxIndex];

                    int ca = candidate.CountUnits(GameManager.GameState) + (int)candidate.kills;
                    int cb = currentMax.CountUnits(GameManager.GameState) + (int)currentMax.kills;

                    if (ca > cb)
                        maxIndex = j;
                    else if (ca == cb && candidate.score > currentMax.score)
                        maxIndex = j;
                }

                if (maxIndex != i)
                {
                    PlayerState temp = players[i];
                    players[i] = players[maxIndex];
                    players[maxIndex] = temp;
                }
            }
            return players;
        }

        public static Il2CppSystem.Collections.Generic.List<PlayerState>? GetPlayersSortedByrushc(
            Il2CppSystem.Collections.Generic.List<PlayerState>? players)
        {
            if (players == null || players.Count <= 1) return players;

            int count = players.Count;
            for (int i = 0; i < count - 1; i++)
            {
                int maxIndex = i;
                for (int j = i + 1; j < count; j++)
                {
                    var a = players[j];
                    var b = players[maxIndex];
                    int ta = AI_2.TechCount(a);
                    int tb = AI_2.TechCount(b);
                    if (ta > tb || (ta == tb && a.score > b.score))
                        maxIndex = j;
                }
                if (maxIndex != i)
                {
                    var tmp = players[i];
                    players[i] = players[maxIndex];
                    players[maxIndex] = tmp;
                }
            }
            return players;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameModeButtonWrapper), nameof(GameModeButtonWrapper.SetData))]
        public static void SetData_GamemodeInfo(
            GameModeButtonWrapper __instance,
            GameMode summaryGameMode,
            GameType gameType,
            int scoreLimit = 10000)
        {
            try
            {
                if (summaryGameMode == EnumCache<GameMode>.GetType("rusha"))
                {
                    __instance.currentGameMode = summaryGameMode;
                    __instance.currentGameType = gameType;
                    __instance.currentGameRules = new GameRules(__instance.currentGameMode);
                    __instance.currentGameRules.ScoreLimit = scoreLimit;

                    __instance.roundButton.text = LocalizationUtils.CapitalizeString("Rush (Star)");
                    Sprite? RushaIcon = PolyMod.Registry.GetSprite("rusha");
                    __instance.roundButton.sprite = RushaIcon;
                }
                else if (summaryGameMode == EnumCache<GameMode>.GetType("rushb"))
                {
                    __instance.currentGameMode = summaryGameMode;
                    __instance.currentGameType = gameType;
                    __instance.currentGameRules = new GameRules(__instance.currentGameMode);
                    __instance.currentGameRules.ScoreLimit = scoreLimit;

                    __instance.roundButton.text = LocalizationUtils.CapitalizeString("Rush (Army)");
                    Sprite? RushbIcon = PolyMod.Registry.GetSprite("rushb");
                    __instance.roundButton.sprite = RushbIcon;
                }
                else if (summaryGameMode == EnumCache<GameMode>.GetType("rushc"))
                {
                    __instance.currentGameMode = summaryGameMode;
                    __instance.currentGameType = gameType;
                    __instance.currentGameRules = new GameRules(__instance.currentGameMode);
                    __instance.currentGameRules.ScoreLimit = scoreLimit;

                    __instance.roundButton.text = LocalizationUtils.CapitalizeString("Rush (Tech)");
                    Sprite? RushcIcon = PolyMod.Registry.GetSprite("rushc");
                    __instance.roundButton.sprite = RushcIcon;
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Rush-Backend] GameModeButtonWrapper SetData: {ex}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameModeButtonWrapper), nameof(GameModeButtonWrapper.OnButtonClicked))]
        public static void OnButtonClicked_GamemodeInfo(
            GameModeButtonWrapper __instance,
            int id,
            UnityEngine.EventSystems.BaseEventData? eventData = null)
        {
            try
            {
                var mode = GameManager.GameState.Settings.RulesGameMode;
                string modeName = "";
                if (mode == EnumCache<GameMode>.GetType("rusha"))
                    modeName = "Rush (Star)";
                else if (mode == EnumCache<GameMode>.GetType("rushb"))
                    modeName = "Rush (Army)";
                else if (mode == EnumCache<GameMode>.GetType("rushc"))
                    modeName = "Rush (Tech)";
                else
                    return;

                BasicPopup basicPopup = PopupManager.GetBasicPopup();
                basicPopup.Header = LocalizationUtils.CapitalizeString(modeName);

                string? text2 = Localization.Get(
                    GameModeUtils.GetDescription(__instance.currentGameMode),
                    (Il2CppReferenceArray<Il2CppSystem.Object>)Array.Empty<Il2CppSystem.Object>());
                basicPopup.Description = text2;
                basicPopup.buttonData = new PopupBase.PopupButtonData[]
                {
                    new PopupBase.PopupButtonData(
                        "buttons.back",
                        PopupBase.PopupButtonData.States.Selected,
                        null, -1, true, null)
                };
                basicPopup.Show(InputManager.GetInputPosition());

                Loader.modLogger?.LogInfo("[Rush-Backend] OnButtonClicked finished!");
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Rush-Backend] GameModeButtonWrapper OnButtonClicked: {ex}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameModeUtils), nameof(GameModeUtils.GetDescription))]
        public static void GetDescription_GamemodeInfo(GameMode gameMode, ref string __result)
        {
            try
            {
                if (gameMode == EnumCache<GameMode>.GetType("rusha"))
                    __result = "gamemode.rusha.description";
                else if (gameMode == EnumCache<GameMode>.GetType("rushb"))
                    __result = "gamemode.rushb.description";
                else if (gameMode == EnumCache<GameMode>.GetType("rushc"))
                    __result = "gamemode.rushc.description";
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Rush-Backend] GameModeUtils error: {ex}");
            }
        }
    }
}