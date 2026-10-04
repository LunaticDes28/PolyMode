using System;
using System.Linq;
using HarmonyLib;
using Il2CppInterop.Runtime;
using PolyMod;
using Polytopia.Data;
using PolytopiaBackendBase.Game;
using TMPro;
using UnityEngine;

namespace PolyMode
{
    public class CitadelOverlay : MonoBehaviour
    {
        public CitadelOverlay(IntPtr handle) : base(handle) { }

        public TextMeshPro? label;
        public SpriteRenderer? background;
        public Transform? contentTransform;

        public void Awake()
        {
            try
            {
                var type = Il2CppType.Of<TextMeshPro>();
                var textComponent = gameObject.AddComponent(type);
                if (textComponent != null)
                    label = textComponent.Cast<TextMeshPro>();

                var bgObj = new GameObject("Background");
                bgObj.transform.SetParent(transform, false);
                var srType = Il2CppType.Of<SpriteRenderer>();
                var addedSr = bgObj.AddComponent(srType);
                if (addedSr != null)
                    background = addedSr.Cast<SpriteRenderer>();

                if (background != null)
                {
                    var bgSprite = Resources.FindObjectsOfTypeAll<Sprite>()
                        .FirstOrDefault(s =>
                            s.name.Contains("panel")
                            || s.name.Contains("box")
                            || s.name.Contains("ui"));

                    if (bgSprite != null)
                        background.sprite = bgSprite;

                    background.color = new Color(0.1f, 0.1f, 0.1f, 0.7f);
                    background.sortingOrder = 29;
                }

                var contentObj = new GameObject("Content");
                contentObj.transform.SetParent(transform, false);
                contentTransform = contentObj.transform;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest] CitadelOverlay.Awake: {ex.Message}");
            }
        }

        public void SetCitadel(Building building, ImprovementData data)
        {
            if (label == null) return;

            if (building == null || data == null)
            {
                label.text = "Citadel";
                return;
            }

            string displayName = "Citadel";

            try
            {
                var tile = building.Tile;
                if (tile?.Data != null
                    && tile.Data.rulingCityCoordinates != WorldCoordinates.NULL_COORDINATES)
                {
                    TileData cityTile = GameManager.GameState.Map.GetTile(
                        tile.Data.rulingCityCoordinates);
                    if (cityTile?.improvement?.name != null)
                        displayName = cityTile.improvement.name;
                }
                else if (!string.IsNullOrEmpty(data.displayName))
                {
                    displayName = Localization.Get(data.displayName);
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning(
                    $"[Conquest] Failed to get citadel city name: {ex.Message}");
            }

            label.text = displayName;

            if (label.fontSharedMaterial != null)
                label.fontSharedMaterial.renderQueue = 4000;

            if (background != null)
            {
                if (background.sprite == null)
                {
                    Texture2D whiteTex = Texture2D.whiteTexture;
                    background.sprite = Sprite.Create(
                        whiteTex,
                        new Rect(0, 0, whiteTex.width, whiteTex.height),
                        new Vector2(0.5f, 0.5f));
                    background.drawMode = SpriteDrawMode.Simple;
                }

                if (building.Owner != null)
                {
                    var playerColor = building.Owner.GetPlayerColor(GameManager.GameState);
                    background.color = ColorUtil.SetAlphaOnColor(playerColor, 0.68f);
                }
            }

            const string LayerName = "Terrain";
            const int LayerId = 1783986775;
            const int fogOrder = 31;

            int orderBg = fogOrder - 2;
            int orderText = fogOrder - 1;

            var meshRenderer = label.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.sortingLayerName = LayerName;
                meshRenderer.sortingLayerID = LayerId;
                meshRenderer.sortingOrder = orderText;
            }

            if (background != null)
            {
                background.sortingLayerName = LayerName;
                background.sortingLayerID = LayerId;
                background.sortingOrder = orderBg;
            }

            label.ForceMeshUpdate(false, false);
            float textWidth = (float)(label.bounds.size.x * 2);

            float paddingX = 0.2f;
            float targetWidth = textWidth + paddingX;
            float targetHeight = 0.4f;

            if (background != null)
                background.transform.localScale = new Vector3(targetWidth, targetHeight, 1f);

            if (contentTransform != null)
            {
                contentTransform.localScale = Vector3.one;
                contentTransform.localPosition = new Vector3(0f, 0f, 0f);
            }
        }
    }

    public class MonumentOverlay : MonoBehaviour
    {
        public MonumentOverlay(IntPtr handle) : base(handle) { }

        public TextMeshPro? label;
        public SpriteRenderer? background;
        public Transform? contentTransform;

        public void Awake()
        {
            try
            {
                var type = Il2CppType.Of<TextMeshPro>();
                var textComponent = gameObject.AddComponent(type);
                if (textComponent != null)
                    label = textComponent.Cast<TextMeshPro>();

                var bgObj = new GameObject("Background");
                bgObj.transform.SetParent(transform, false);
                var srType = Il2CppType.Of<SpriteRenderer>();
                var addedSr = bgObj.AddComponent(srType);
                if (addedSr != null)
                    background = addedSr.Cast<SpriteRenderer>();

                if (background != null)
                {
                    var bgSprite = Resources.FindObjectsOfTypeAll<Sprite>()
                        .FirstOrDefault(s =>
                            s.name.Contains("panel")
                            || s.name.Contains("box")
                            || s.name.Contains("ui"));

                    if (bgSprite != null)
                        background.sprite = bgSprite;

                    background.color = new Color(0.1f, 0.1f, 0.1f, 0.7f);
                    background.sortingOrder = 29;
                }

                var contentObj = new GameObject("Content");
                contentObj.transform.SetParent(transform, false);
                contentTransform = contentObj.transform;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Rush] MonumentOverlay.Awake: {ex.Message}");
            }
        }

        public void SetTurns(int turnsLeft, bool blocked, Building building)
        {
            if (label == null) return;

            label.text = blocked ? $"(X) {turnsLeft}" : $"(O) {turnsLeft}";

            if (label.fontSharedMaterial != null)
                label.fontSharedMaterial.renderQueue = 4000;

            if (background != null)
            {
                if (background.sprite == null)
                {
                    Texture2D whiteTex = Texture2D.whiteTexture;
                    background.sprite = Sprite.Create(
                        whiteTex,
                        new Rect(0, 0, whiteTex.width, whiteTex.height),
                        new Vector2(0.5f, 0.5f));
                    background.drawMode = SpriteDrawMode.Simple;
                }

                if (building?.Owner != null)
                {
                    var playerColor = building.Owner.GetPlayerColor(GameManager.GameState);
                    background.color = ColorUtil.SetAlphaOnColor(playerColor, 0.68f);
                }
            }

            const string LayerName = "Terrain";
            const int LayerId = 1783986775;
            const int fogOrder = 31;

            int orderBg = fogOrder - 2;
            int orderText = fogOrder - 1;

            var meshRenderer = label.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.sortingLayerName = LayerName;
                meshRenderer.sortingLayerID = LayerId;
                meshRenderer.sortingOrder = orderText;
            }

            if (background != null)
            {
                background.sortingLayerName = LayerName;
                background.sortingLayerID = LayerId;
                background.sortingOrder = orderBg;
            }

            label.ForceMeshUpdate(false, false);
            float textWidth = (float)(label.bounds.size.x * 2);

            float paddingX = 0.2f;
            float targetWidth = textWidth + paddingX;
            float targetHeight = 0.4f;

            if (background != null)
                background.transform.localScale = new Vector3(targetWidth, targetHeight, 1f);

            if (contentTransform != null)
            {
                contentTransform.localScale = Vector3.one;
                contentTransform.localPosition = new Vector3(0f, 0f, 0f);
            }
        }
    }

    public class OverlayPatches
    {
        static bool IsCitadelMode()
        {
            var gs = GameManager.GameState;
            if (gs?.Settings == null) return false;
            var mode = gs.Settings.RulesGameMode;
            return mode == EnumCache<GameMode>.GetType("conquest")
                || mode == EnumCache<GameMode>.GetType("reign");
        }

        static Transform? FindCitadelOverlay(Building building)
        {
            if (building?.transform == null) return null;
            return building.transform.Find("CitadelOverlay");
        }

        static void SetCitadelOverlayVisible(Building building, bool visible)
        {
            var t = FindCitadelOverlay(building);
            if (t == null) return;

            bool show = visible && GameManager.debugShowGameUI;
            if (t.gameObject.activeSelf != show)
                t.gameObject.SetActive(show);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Building), nameof(Building.SetData))]
        public static void Building_SetData_Citadel(Building __instance, ImprovementData data)
        {
            try
            {
                if (!IsCitadelMode()) return;
                if (__instance == null || data == null) return;
                if (data.type != EnumCache<ImprovementData.Type>.GetType("citadel")) return;
                if (data.type == ImprovementData.Type.City) return;
                if (__instance.transform == null) return;
                if (__instance.transform.Find("CitadelOverlay") != null) return;

                var vanillaDisplay = ObjectPool.GetPooledObject<CityStatusDisplay>("CityStatusDisplay");
                if (vanillaDisplay == null) return;

                Sprite? officialBgSprite = null;
                TMP_FontAsset? officialFont = null;
                Material? officialFontMaterial = null;

                try
                {
                    if (vanillaDisplay.nameContainer != null)
                    {
                        if (vanillaDisplay.nameContainer.bg != null)
                            officialBgSprite = vanillaDisplay.nameContainer.bg.sprite;

                        if (vanillaDisplay.nameContainer.label != null)
                        {
                            officialFont = vanillaDisplay.nameContainer.label.font;
                            officialFontMaterial = vanillaDisplay.nameContainer.label.fontSharedMaterial;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Loader.modLogger?.LogWarning(
                        $"[Conquest] Failed to extract CityStatusDisplay assets: {ex.Message}");
                }

                try { vanillaDisplay.ReturnToPool(); }
                catch { }

                var overlayObj = new GameObject("CitadelOverlay");
                var overlayType = Il2CppType.Of<CitadelOverlay>();
                var added = overlayObj.AddComponent(overlayType);
                if (added == null)
                {
                    UnityEngine.Object.Destroy(overlayObj);
                    return;
                }

                var overlay = added.Cast<CitadelOverlay>();
                overlayObj.transform.SetParent(__instance.transform, false);
                overlayObj.transform.rotation = Quaternion.identity;
                overlayObj.transform.localScale = Vector3.one;
                overlayObj.transform.localPosition = new Vector3(0f, -0.1f, 0f);

                if (officialBgSprite != null && overlay.background != null)
                {
                    overlay.background.sprite = officialBgSprite;
                    overlay.background.drawMode = SpriteDrawMode.Sliced;
                }

                if (overlay.label != null)
                {
                    if (officialFont != null)
                        overlay.label.font = officialFont;
                    if (officialFontMaterial != null)
                        overlay.label.fontSharedMaterial = officialFontMaterial;

                    overlay.label.fontSize = 1.25f;
                    overlay.label.alignment = TextAlignmentOptions.Center;
                    overlay.label.fontStyle = FontStyles.Normal;
                }

                overlay.SetCitadel(__instance, data);

                bool explored = __instance.Tile != null && !__instance.Tile.IsHidden;
                SetCitadelOverlayVisible(__instance, explored);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest] Building.SetData citadel: {ex}");
            }
        }

        static void SyncCitadelOverlayVisibility(Building building)
        {
            try
            {
                if (!IsCitadelMode()) return;
                if (building == null || building.Tile == null) return;
                if (FindCitadelOverlay(building) == null) return;

                bool explored = !building.Tile.IsHidden;
                SetCitadelOverlayVisible(building, explored);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning(
                    $"[Conquest] Building.UpdateObject citadel: {ex.Message}");
            }
        }

        static bool IsMonumentType(ImprovementData.Type t)
        {
            try { return t.IsMonument(); }
            catch
            {
                return t >= ImprovementData.Type.Monument1
                    && t <= ImprovementData.Type.Monument7;
            }
        }

        static bool IsRushTechMode()
        {
            var gs = GameManager.GameState;
            if (gs?.Settings == null) return false;
            return gs.Settings.RulesGameMode == EnumCache<GameMode>.GetType("rushc");
        }

        static Transform? FindMonumentOverlay(Building building)
        {
            if (building?.transform == null) return null;
            return building.transform.Find("MonumentOverlay");
        }

        static void SetMonumentOverlayVisible(Building building, bool visible)
        {
            var t = FindMonumentOverlay(building);
            if (t == null) return;

            bool show = visible && GameManager.debugShowGameUI;
            if (t.gameObject.activeSelf != show)
                t.gameObject.SetActive(show);
        }

        static void ApplyMonumentTurns(
            Building building, MonumentOverlay overlay, bool blockedOnly = false)
        {
            if (overlay == null || building == null) return;
            try
            {
                bool blocked;
                int turns = Rush.AI_2.GetResearchTurnsDisplay(
                    building.Tile.Data, GameManager.GameState, blockedOnly, out blocked);
                overlay.SetTurns(turns, blocked, building);
            }
            catch
            {
                overlay.SetTurns(0, true, building);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Building), nameof(Building.SetData))]
        public static void Building_SetData_Monument(Building __instance, ImprovementData data)
        {
            try
            {
                if (!IsRushTechMode()) return;
                if (__instance == null || data == null) return;
                if (!IsMonumentType(data.type)) return;
                if (__instance.transform == null) return;
                if (__instance.transform.Find("MonumentOverlay") != null) return;

                var vanillaDisplay = ObjectPool.GetPooledObject<CityStatusDisplay>("CityStatusDisplay");
                if (vanillaDisplay == null) return;

                Sprite? officialBgSprite = null;
                TMP_FontAsset? officialFont = null;
                Material? officialFontMaterial = null;

                try
                {
                    if (vanillaDisplay.nameContainer != null)
                    {
                        if (vanillaDisplay.nameContainer.bg != null)
                            officialBgSprite = vanillaDisplay.nameContainer.bg.sprite;

                        if (vanillaDisplay.nameContainer.label != null)
                        {
                            officialFont = vanillaDisplay.nameContainer.label.font;
                            officialFontMaterial = vanillaDisplay.nameContainer.label.fontSharedMaterial;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Loader.modLogger?.LogWarning(
                        $"[Rush] Failed to extract CityStatusDisplay assets: {ex.Message}");
                }

                try { vanillaDisplay.ReturnToPool(); }
                catch { }

                var overlayObj = new GameObject("MonumentOverlay");
                var overlayType = Il2CppType.Of<MonumentOverlay>();
                var added = overlayObj.AddComponent(overlayType);
                if (added == null)
                {
                    UnityEngine.Object.Destroy(overlayObj);
                    return;
                }

                var overlay = added.Cast<MonumentOverlay>();
                overlayObj.transform.SetParent(__instance.transform, false);
                overlayObj.transform.rotation = Quaternion.identity;
                overlayObj.transform.localScale = Vector3.one;
                overlayObj.transform.localPosition = new Vector3(0f, -0.1f, 0f);

                if (officialBgSprite != null && overlay.background != null)
                {
                    overlay.background.sprite = officialBgSprite;
                    overlay.background.drawMode = SpriteDrawMode.Sliced;
                }

                if (overlay.label != null)
                {
                    if (officialFont != null)
                        overlay.label.font = officialFont;
                    if (officialFontMaterial != null)
                        overlay.label.fontSharedMaterial = officialFontMaterial;

                    overlay.label.fontSize = 1.25f;
                    overlay.label.alignment = TextAlignmentOptions.Center;
                    overlay.label.fontStyle = FontStyles.Normal;
                }

                ApplyMonumentTurns(__instance, overlay, blockedOnly: false);

                bool explored = __instance.Tile != null && !__instance.Tile.IsHidden;
                SetMonumentOverlayVisible(__instance, explored);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Rush] Building.SetData monument: {ex}");
            }
        }

        // UpdateObject: visibility + cached label (does not advance other players)
        static void SyncMonumentOverlay(Building building)
        {
            try
            {
                if (!IsRushTechMode()) return;
                if (building == null || building.Tile == null) return;

                var t = FindMonumentOverlay(building);
                if (t == null) return;

                var overlay = t.GetComponent<MonumentOverlay>();
                if (overlay != null)
                    ApplyMonumentTurns(building, overlay, blockedOnly: false);

                bool explored = !building.Tile.IsHidden;
                SetMonumentOverlayVisible(building, explored);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning(
                    $"[Rush] Building.UpdateObject monument: {ex.Message}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Building), nameof(Building.UpdateObject), new Type[] { })]
        public static void Building_UpdateObject(Building __instance)
        {
            SyncCitadelOverlayVisibility(__instance);
            SyncMonumentOverlay(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Building), nameof(Building.UpdateObject),
            new Type[] { typeof(MapRenderContext), typeof(SkinVisualsTransientData) })]
        public static void Building_UpdateObject_Ctx(
            Building __instance,
            MapRenderContext ctx,
            SkinVisualsTransientData transientSkinData)
        {
            SyncCitadelOverlayVisibility(__instance);
            SyncMonumentOverlay(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Building), nameof(Building.SetVisible))]
        public static void Building_SetVisible(Building __instance, bool value)
        {
            try
            {
                if (__instance == null) return;

                bool explored = __instance.Tile != null && !__instance.Tile.IsHidden;

                if (IsCitadelMode() && FindCitadelOverlay(__instance) != null)
                    SetCitadelOverlayVisible(__instance, value && explored);

                if (IsRushTechMode() && FindMonumentOverlay(__instance) != null)
                    SetMonumentOverlayVisible(__instance, value && explored);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning(
                    $"[PolyMode] Building.SetVisible overlay: {ex.Message}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MoveAction), nameof(MoveAction.ExecuteDefault))]
        public static void MoveAction_ExecuteDefault(MoveAction __instance, GameState gameState)
        {
            try
            {
                if (!IsRushTechMode()) return;
                if (gameState?.Map == null || __instance?.Path == null || __instance.Path.Count == 0)
                    return;

                WorldCoordinates to = __instance.Path[0];
                WorldCoordinates from = __instance.Path[__instance.Path.Count - 1];

                RefreshMonumentsInCity(gameState, from, blockedOnly: true);
                if (from != to)
                    RefreshMonumentsInCity(gameState, to, blockedOnly: true);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning($"[Rush] MoveAction monument overlay: {ex.Message}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(TrainAction), nameof(TrainAction.ExecuteDefault))]
        public static void TrainAction_ExecuteDefault(TrainAction __instance, GameState gameState)
        {
            try
            {
                if (!IsRushTechMode()) return;
                if (gameState?.Map == null) return;

                RefreshMonumentsInCity(gameState, __instance.Coordinates, blockedOnly: true);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning($"[Rush] TrainAction monument overlay: {ex.Message}");
            }
        }

        static void RefreshMonumentsInCity(
            GameState state, WorldCoordinates coords, bool blockedOnly)
        {
            TileData tile = state.Map.GetTile(coords);
            if (tile == null) return;

            WorldCoordinates cityCoords =
                (tile.improvement != null && tile.improvement.type == ImprovementData.Type.City)
                    ? tile.coordinates
                    : tile.rulingCityCoordinates;

            if (cityCoords == WorldCoordinates.NULL_COORDINATES) return;

            TileData city = state.Map.GetTile(cityCoords);
            if (city?.improvement == null) return;
            if (!Rush.AI_2.CityHasMonument(state, city)) return;

            var all = UnityEngine.Object.FindObjectsOfType<Building>();
            if (all == null) return;

            foreach (var building in all)
            {
                if (building?.Tile?.Data == null) continue;
                var d = building.Tile.Data;
                if (d.improvement == null) continue;
                if (!IsMonumentType(d.improvement.type)) continue;
                if (d.rulingCityCoordinates != cityCoords) continue;

                var t = FindMonumentOverlay(building);
                if (t == null) continue;
                var overlay = t.GetComponent<MonumentOverlay>();
                if (overlay != null)
                    ApplyMonumentTurns(building, overlay, blockedOnly);

                if (building.Tile != null)
                    SetMonumentOverlayVisible(building, !building.Tile.IsHidden);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(StartTurnAction), nameof(StartTurnAction.ExecuteDefault))]
        public static void StartTurnAction_ExecuteDefault_Refresh(
            StartTurnAction __instance, GameState gameState)
        {
            try
            {
                if (!IsRushTechMode() || __instance == null || gameState == null) return;
                // AI_2 tick already commits; commit again is idempotent
                Rush.AI_2.CommitDisplayForPlayer(gameState, __instance.PlayerId);
                RefreshMonumentOverlaysForPlayer(__instance.PlayerId);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning($"[Rush] StartTurn overlay: {ex.Message}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(EndTurnAction), nameof(EndTurnAction.Execute))]
        public static void EndTurnAction_Execute_Refresh(EndTurnAction __instance, GameState state)
        {
            try
            {
                if (!IsRushTechMode() || __instance == null || state == null) return;
                Rush.AI_2.CommitDisplayForPlayer(state, __instance.PlayerId);
                RefreshMonumentOverlaysForPlayer(__instance.PlayerId);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning($"[Rush] EndTurn overlay: {ex.Message}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ExpandCityAction), nameof(ExpandCityAction.ExecuteDefault))]
        public static void ExpandCityAction_ExecuteDefault_Refresh(ExpandCityAction __instance, GameState state)
        {
            try
            {
                if (!IsRushTechMode() || __instance == null || state == null) return;
                Rush.AI_2.CommitDisplayForPlayer(state, __instance.PlayerId);
                RefreshMonumentOverlaysForPlayer(__instance.PlayerId);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning($"[Rush] EndTurn overlay: {ex.Message}");
            }
        }

        public static void RefreshMonumentOverlaysForPlayer(byte playerId)
        {
            if (!IsRushTechMode()) return;
            if (playerId == 0 || playerId == 255) return;

            var all = UnityEngine.Object.FindObjectsOfType<Building>();
            if (all == null) return;

            foreach (var building in all)
            {
                if (building?.Tile?.Data?.improvement == null) continue;
                if (!IsMonumentType(building.Tile.Data.improvement.type)) continue;
                if (building.Tile.Data.owner != playerId) continue;

                SyncMonumentOverlay(building);
            }
        }

        public static void RefreshAllMonumentOverlays()
        {
            if (!IsRushTechMode()) return;

            var all = UnityEngine.Object.FindObjectsOfType<Building>();
            if (all == null) return;

            foreach (var building in all)
            {
                if (building?.Tile?.Data?.improvement == null) continue;
                if (!IsMonumentType(building.Tile.Data.improvement.type)) continue;
                SyncMonumentOverlay(building);
            }
        }
    }
}