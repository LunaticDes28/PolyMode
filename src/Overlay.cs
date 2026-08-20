using UnityEngine;
using TMPro;
using PolyMod;
using Polytopia.Data;
using PolytopiaBackendBase.Game;
using HarmonyLib;
using Il2CppInterop.Runtime;
using System;
using System.Linq;

namespace PolyMode
{
    public class CitadelOverlay : MonoBehaviour
    {
        /*private static void ProbeFogSort()
        {
            if (fogProbeDone) return;
            fogProbeDone = true;

            try
            {
                var renderers = UnityEngine.Object.FindObjectsOfType<SpriteRenderer>();
                SpriteRenderer? best = null;
                int bestOrder = int.MinValue;

                foreach (var sr in renderers)
                {
                    if (sr == null) continue;
                    string n = sr.gameObject.name ?? "";
                    string layer = sr.sortingLayerName ?? "";
                    // adjust names if logs show something else
                    if (n.IndexOf("fog", StringComparison.OrdinalIgnoreCase) < 0
                        && layer.IndexOf("fog", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    if (sr.sortingOrder >= bestOrder)
                    {
                        bestOrder = sr.sortingOrder;
                        best = sr;
                    }
                }

                if (best != null)
                {
                    fogLayerName = best.sortingLayerName;
                    fogLayerId = best.sortingLayerID;
                    fogOrder = best.sortingOrder;
                    // Loader.modLogger?.LogInfo($"[Citadel] Fog sort: layer={fogLayerName} id={fogLayerId} order={fogOrder}");
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning($"[Citadel] Fog probe failed: {ex.Message}");
            }
        }*/

        public CitadelOverlay(IntPtr handle) : base(handle) { }
        public TextMeshPro? label;
        public SpriteRenderer? background;
        public Transform? contentTransform;

        public void Awake()
        {
            try
            {
                // 1. TextMeshPro 初始化
                var type = Il2CppType.Of<TextMeshPro>();
                var textComponent = gameObject.AddComponent(type);
                if (textComponent != null)
                {
                    label = textComponent.Cast<TextMeshPro>();
                }

                // 2. 使用 IL2CPP 安全非泛型寫法掛載 SpriteRenderer
                var bgObj = new GameObject("Background");
                bgObj.transform.SetParent(transform, false);
                
                var srType = Il2CppType.Of<SpriteRenderer>();
                var addedSr = bgObj.AddComponent(srType);
                if (addedSr != null)
                {
                    background = addedSr.Cast<SpriteRenderer>();
                }

                if (background != null)
                {
                    var bgSprite = Resources.FindObjectsOfTypeAll<Sprite>()
                        .FirstOrDefault(s => s.name.Contains("panel") || s.name.Contains("box") || s.name.Contains("ui"));

                    if (bgSprite != null) background.sprite = bgSprite;
                    background.color = new Color(0.1f, 0.1f, 0.1f, 0.7f);
                    background.sortingOrder = 95; 
                }

                // 3. Content 節點建立
                var contentObj = new GameObject("Content");
                contentObj.transform.SetParent(transform, false);
                contentTransform = contentObj.transform;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest] Error in Citadel Overlay Awake: {ex.Message}");
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
                if (tile?.Data != null && tile.Data.rulingCityCoordinates != WorldCoordinates.NULL_COORDINATES)
                {
                    TileData cityTile = GameManager.GameState.Map.GetTile(tile.Data.rulingCityCoordinates);
                    
                    if (cityTile?.improvement?.name != null)
                    {
                        displayName = cityTile.improvement.name;   
                    }
                }
                else if (!string.IsNullOrEmpty(data.displayName))
                {
                    displayName = Localization.Get(data.displayName);
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning($"[Conquest] Failed to get citadel city name: {ex.Message}");
            }

            label.text = displayName;

            // ⭕ 【第一步：優化化簡——移除非精準的隨機字型搜尋】
            string officialLayerName = "Default";
            int officialLayerID = 0;

            var parentSr = building.GetComponent<SpriteRenderer>() ?? building.GetComponentInChildren<SpriteRenderer>();
            if (parentSr != null)
            {
                officialLayerName = parentSr.sortingLayerName;
                officialLayerID = parentSr.sortingLayerID;
            }

            if (label.fontSharedMaterial != null)
            {
                label.fontSharedMaterial.renderQueue = 4000;
            }

            // ⭕ 【第二步：核心重構——直接在原地建立 100% 標準長方形幾何體】
            if (background != null)
            {
                // 如果上面 Postfix 沒偷成功（Sprite 仍為空），才啟用純白長方形保底
                if (background.sprite == null)
                {
                    Texture2D whiteTex = Texture2D.whiteTexture; 
                    background.sprite = Sprite.Create(whiteTex, new Rect(0, 0, whiteTex.width, whiteTex.height), new Vector2(0.5f, 0.5f));
                    background.drawMode = SpriteDrawMode.Simple; 
                }

                // 注意：官方的自訂背景可能帶有特殊的透明度 Shader，如果換了官方 Sprite 發現變全黑或沒隱藏，
                // 可以嘗試把下面這行 Canvas 材質註解掉，改用原本圖片帶有的預設材質
                // background.material = Canvas.GetDefaultCanvasMaterial();

                if (building.Owner != null)
                {
                    var playerColor = building.Owner.GetPlayerColor(GameManager.GameState);
                    background.color = ColorUtil.SetAlphaOnColor(playerColor, 0.68f);
                }
            }

            // ⭕ 【第三步：動態 Depth 層級設定】
            //ProbeFogSort();

            const string LayerName = "Terrain";
            const int LayerId = 1783986775;
            const int fogOrder = 31;

            int orderBg = fogOrder - 2;   // 29
            int orderText = fogOrder - 1; // 30

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

            // ⭕ 【第四步：將 UpdateOverlaySize 邏輯全面攤平融入尾端（不依賴外部私有方法呼交）】
            label.ForceMeshUpdate(false, false);
            float textWidth = (float)(label.bounds.size.x * 2); 

            // 💡 參數調整：現在背景是絕對純粹的長方形，1.0f 即為 Unity 標準的一個世界座標方格大小
            float paddingX = 0.2f;             // 文字左右兩側要留白的世界單位寬度
            float targetWidth = textWidth + paddingX;
            float targetHeight = 0.4f;          // 長方形的絕對物理高度

            if (background != null)
            {
                // 由於底層是 1x1 頂點四邊形，直接使用 localScale 來決定長方形的精準物理寬高
                background.transform.localScale = new Vector3(targetWidth, targetHeight, 1f);
            }

            // 處理整個文字與背景容器的水平置中對齊
            if (contentTransform != null)
            {
                contentTransform.localScale = Vector3.one;
                contentTransform.localPosition = new Vector3(-targetWidth * 0f, 0f, 0f);
            }
        }
    }

    public class CitadelOverlayPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Building), nameof(Building.SetData))]
        public static void Building_SetData(Building __instance, ImprovementData data)
        {
            try
            {
                // ===== Safe GameState / mode check =====
                if (GameManager.GameState == null || GameManager.GameState.Settings == null)
                    return;

                var mode = GameManager.GameState.Settings.RulesGameMode;
                if (mode != EnumCache<GameMode>.GetType("conquest")
                    && mode != EnumCache<GameMode>.GetType("reign"))
                    return;

                if (__instance == null || data == null)
                    return;

                if (data.type != EnumCache<ImprovementData.Type>.GetType("citadel"))
                    return;

                if (data.type == ImprovementData.Type.City)
                    return;

                if (__instance.transform == null)
                    return;

                if (__instance.transform.Find("CitadelOverlay") != null)
                    return;

                // 1. Get vanilla CityStatusDisplay
                var vanillaDisplay = ObjectPool.GetPooledObject<CityStatusDisplay>("CityStatusDisplay");
                if (vanillaDisplay == null)
                    return;

                Sprite? officialBgSprite = null;
                TMP_FontAsset? officialFont = null;
                Material? officialFontMaterial = null;

                // 2. Extract font / bg safely
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
                    Loader.modLogger?.LogWarning($"[Conquest] Failed to extract CityStatusDisplay assets: {ex.Message}");
                }

                // 3. Return to pool
                try
                {
                    vanillaDisplay.ReturnToPool();
                }
                catch { /* ignore pool errors */ }

                // 4. Create overlay
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

                // 5. Apply official font
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
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest] Building SetData error: {ex}");
            }
        }
    }
}