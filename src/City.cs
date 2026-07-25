using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSystem.Collections.Generic;
using PolyMod;
using Polytopia.Data;
using PolytopiaBackendBase.Game;
using PolytopiaBackendBase.Common;
using UnityEngine;
using Il2CppSystem.Linq;

namespace PolyMode
{
    public static class City
    {
        public class CityRequirement
        {
            public int level { get; set; }
            public bool notCapital { get; set; }
        }

        public class CityRewardExtensions
        {
            public string? id { get; set; }
            public System.Collections.Generic.List<CityRequirement>? cityRequirements { get; set; }
        }

        public static class CityRewardExtensionsManager
        {
            public static System.Collections.Generic.Dictionary<string, CityRewardExtensions> CustomExtensions = new System.Collections.Generic.Dictionary<string, CityRewardExtensions>();
            public static void RegisterFromJObject(JObject patch)
            {
                try
                {
                    if (patch == null || patch["cityReward"] == null) return;
                    Loader.modLogger?.LogInfo($"[Conquest-Hook] Successfully dynamic-mapped cityReward");

                    var cityRewardNode = patch["cityReward"];
                    Loader.modLogger?.LogInfo($"[Conquest-Hook] Raw node type: {cityRewardNode?.GetType()?.Name}");

                    JObject? jObj = cityRewardNode.Cast<JObject>();
                    if (jObj != null)
                    {
                        Loader.modLogger?.LogInfo($"[Conquest-Hook] node conversion success, item count: {jObj.Count}");
                        JProperty[] properties = jObj.Properties().ToArray();

                        for (int i = 0; i < properties.Length; i++)
                        {
                            JProperty property = properties[i];
                            if (property == null) continue;

                            string id = property.Name;
                            JToken rewardData = property.Value;
                            Loader.modLogger?.LogInfo($"[Conquest-Hook] Successfully dynamic-mapped ID {id}");

                            if (rewardData == null) continue;
                            Loader.modLogger?.LogInfo($"[Conquest-Hook] cityReward not null");

                            CityRewardExtensions extension = new CityRewardExtensions();
                            extension.cityRequirements = new System.Collections.Generic.List<CityRequirement>();

                            Loader.modLogger?.LogInfo($"[Conquest-Hook] CityRequirement initialized");

                            var reqToken = rewardData["cityRequirements"] ?? rewardData["CityRequirements"];

                            if (reqToken != null && reqToken.Type == JTokenType.Array)
                            {
                                Loader.modLogger?.LogInfo($"[Conquest-Hook] Found cityRequirements token {reqToken}");

                                JArray reqArray = reqToken.Cast<JArray>();
                                Loader.modLogger?.LogInfo($"[Conquest-Hook] Found cityRequirements array with {reqArray.Count} items");

                                for (int j = 0; j < reqArray.Count; j++)
                                {
                                    JToken item = reqArray[j];
                                    if (item == null) continue;

                                    int reqLevel = 0;
                                    bool reqNotCapital = false;
                                    
                                    if (item["level"] != null)
                                    {
                                        string levelStr = item["level"].ToString();
                                        int.TryParse(levelStr, out reqLevel);
                                    }

                                    if (item["notCapital"] != null)
                                    {
                                        string capStr = item["notCapital"].ToString();
                                        bool.TryParse(capStr, out reqNotCapital);
                                    }

                                    CityRequirement req = new CityRequirement
                                    {
                                        level = reqLevel,
                                        notCapital = reqNotCapital
                                    };
                                    extension.cityRequirements.Add(req);
                                    Loader.modLogger?.LogInfo($"[Conquest-Hook] Parsed req -> level: {reqLevel}, notCapital: {reqNotCapital}");
                                }
                            }

                            if (extension != null)
                            {
                                extension.id = id;
                                CustomExtensions[id] = extension;

                                Loader.modLogger?.LogInfo($"[Conquest-Hook] Successfully dynamic-mapped reward via JObject: '{id}' to Enum ID: {extension.id}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Loader.modLogger?.LogError($"[Conquest-Hook] Error mapping cityReward from JObject: {ex}");
                }
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PolyMod.Loader), nameof(PolyMod.Loader.LoadGameLogicDataPatch))]
        public static void CityRewardPatch_ThirdOption(Mod mod, JObject gld, JObject patch)
        {
            try
            {
                if (patch != null)
                {
                    Loader.modLogger?.LogInfo($"[Conquest-Hook] Dependency finished loading patch for {mod?.id}. Intercepting cityReward...");
                    
                    CityRewardExtensionsManager.RegisterFromJObject(patch);
                }
            }
            catch (System.Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Hook] Critical error in LoadGameLogicDataPatch Prefix: {ex}");
            }
        }
        
        [HarmonyPrefix]
        [HarmonyPatch(typeof(CommandTriggerUIUtils), nameof(CommandTriggerUIUtils.ShowCommandTrigger))]
        public static bool ShowCommandTrigger_ThirdOption(CommandTrigger commandTrigger)
        {
            if (GameManager.GameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                && GameManager.GameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
            {
                return true;
            }

            PlayerState playerState;
            GameManager.GameState.TryGetPlayer(GameManager.GameState.CurrentPlayer, out playerState);
            CommandTriggerType type = commandTrigger.type;

            if (PopupManager.IsPopupShowing<RewardPopup>(null))
            {
                return false;
            }
            RewardPopup rewardPopup = PopupManager.GetRewardPopup();
            rewardPopup.RewardChoosenCallback = new Action<TileData, CityReward>(CommandTriggerUIUtils.PerformCityRewardAction);
            ImprovementData improvementData;
            if (!GameManager.GameState.GameLogicData.TryGetData(ImprovementData.Type.City, out improvementData))
            {
                return false;
            }

            TileData tile = GameManager.GameState.Map.GetTile(commandTrigger.coordinates);
            if (playerState.Id != tile.owner)
            {
                return false;
            }

            CityReward[] allRewards = ImprovementDataExtensions.GetCityRewardsForLevel(improvementData, tile.improvement.level - 1);

            CityReward[] notMyRewards = allRewards
                .Where(reward => !CityRewardExtensionsManager.CustomExtensions.ContainsKey(reward.GetName()))
                .ToArray();

            CityReward[] myModRewards = GetCustomCityRewards(tile);

            CityReward[] cityRewardsForLevel = notMyRewards
                .Concat(myModRewards)
                .Distinct()
                .ToArray();
            
            // CityReward[] cityRewardsForLevel = GetCustomCityRewards(tile);
            rewardPopup.SetData(playerState, tile, cityRewardsForLevel, RewardPopup.PopupType.CityLevelUp, false);
            rewardPopup.Show();
            AudioManager.PlaySFX(SFXTypes.RewardStart, 0, 1f, 1f, 0f);
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ImprovementDataExtensions), nameof(ImprovementDataExtensions.GetCityRewardsForLevel))]
        public static void GetCityRewardsForLevel_ThirdOption(ref Il2CppStructArray<CityReward> __result, ImprovementData data, int level)
        {
            if (level == 1)
            {
                __result = new CityReward[]
                {
                CityReward.Explorer,
                CityReward.Workshop,
                EnumCache<CityReward>.GetType("evacuation")
                };
            }
            else
            if (level == 2)
            {
                __result = new CityReward[]
                {
                CityReward.CityWall,
                CityReward.Resources,
                EnumCache<CityReward>.GetType("valhalla")
                };
            }
            else
            if (level == 3)
            {
                __result = new CityReward[]
                {
                CityReward.BorderGrowth,
                CityReward.PopulationGrowth,
                EnumCache<CityReward>.GetType("taxreform")
                };
            }
            else
            if (level >= 4)
            {
                __result = new CityReward[]
                {
                CityReward.Park,
                CityReward.SuperUnit,
                };
            }
        }

        public static CityReward[] GetCustomCityRewards(TileData tile)
        {
            if (tile == null || tile.improvement == null)
            {
                Loader.modLogger?.LogWarning("[Conquest-Reward] GetCustomCityRewards received a null tile or improvement.");
                return Array.Empty<CityReward>();
            }

            Il2CppSystem.Collections.Generic.List<CityReward> list = new Il2CppSystem.Collections.Generic.List<CityReward>();

            CityReward[] rewards = (CityReward[])Enum.GetValues(typeof(CityReward));
            Loader.modLogger?.LogInfo($"Enum count: {rewards.Length}");
            
            foreach (var kvp in CityRewardExtensionsManager.CustomExtensions)
            {
                CityReward customReward = EnumCache<CityReward>.GetType(kvp.Key); 
                CityRewardExtensions extension = kvp.Value;

                if (extension != null && extension.cityRequirements != null)
                {
                    Loader.modLogger?.LogInfo($"[Conquest-Reward] Evaluating Registered Reward: {kvp.Key} (True Enum ID: {(int)customReward})");

                    bool meetsRequirements = false;

                    foreach (var req in extension.cityRequirements)
                    {
                        Loader.modLogger?.LogInfo($"LReq: {req.level}");
                        Loader.modLogger?.LogInfo($"CReq: {req.notCapital}");

                        bool levelMatch = tile.improvement.level == req.level + 1;

                        bool capitalMatch = !req.notCapital || tile.capitalOf == 0;

                        Loader.modLogger?.LogInfo($"Level: {levelMatch}");
                        Loader.modLogger?.LogInfo($"Capital: {capitalMatch}");


                        if (levelMatch && capitalMatch)
                        {
                            meetsRequirements = true;
                            break;
                        }
                    }

                    if (meetsRequirements && !list.Contains(customReward))
                    {
                        list.Add(customReward);
                        Loader.modLogger?.LogInfo($"[Conquest-Reward] City meets extension criteria. Added custom reward ID: {kvp.Key}");
                    }
                }
            }

            return list.ToArray();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIIconData), nameof(UIIconData.GetSprite))]
        public static void Override(UIIconData __instance, ref Sprite __result, string id)
        {
            if (!isCustomReward(id))
                return;

            CityReward rewardType = getEnum(id);
            string? spriteName = EnumCache<CityReward>.GetName(rewardType);

            if (string.IsNullOrEmpty(spriteName))
                return;

            Sprite? sprite = Registry.GetSprite(spriteName, "", 0);
            
            if (sprite != null)
            {
                __result = sprite;
            }
        }

        public static bool isCustomReward(string s)
        {
            string[] array = s.Split("_");
            if (array[1] != "rewards")
            {
                return false;
            }
            if (int.TryParse(array[2], out var _))
            {
                return true;
            }
            return false;
        }

        public static CityReward getEnum(string s)
        {
            return (CityReward)int.Parse(s.Split("_")[2]);
        }


        [HarmonyPostfix]
        [HarmonyPatch(typeof(CityRewardAction), nameof(CityRewardAction.Execute))]
        public static void CustomRewards(CityRewardAction __instance, GameState state)
        {
            TileData tile = state.Map.GetTile(__instance.Coordinates);
            if (tile == null || tile.improvement == null)
            {
                return;
            }
            PlayerState playerState;
            if (!state.TryGetPlayer(tile.owner, out playerState) || !GameManager.GameState.GameLogicData.TryGetData(playerState.tribe, out TribeData tribeData))
            {
                return;
            }

            Loader.modLogger?.LogInfo($"[Conquest-Reward] Running reward {__instance.Reward}");

            if (__instance.Reward == EnumCache<CityReward>.GetType("evacuation"))
            {
                Loader.modLogger?.LogInfo($"[Conquest-Reward] Running custom evacuation");
                Main.DestroyCityConquest(state, tile, playerState, true);
            }
            else
            {
                if (__instance.Reward == EnumCache<CityReward>.GetType("valhalla"))
                {
                    return;
                }
                if (__instance.Reward == EnumCache<CityReward>.GetType("taxreform"))
                {
                    return;
                }
            }
        }

        // =========================================================================
        // B. Valhalla
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(CityRenderer), nameof(CityRenderer.RefreshCity))]
        public static void Valhalla_Render(CityRenderer __instance)
        {
            if (__instance.dataChanged)
            {
                return;
            }
            var a = GameManager.GameState.Map.GetTile(__instance.Coordinates);
            bool hasVal = a.improvement.HasReward(EnumCache<CityReward>.GetType("valhalla"));

            if (hasVal)
            {
                PolytopiaBackendBase.Common.TribeType tribe = __instance.Tribe;
                PolytopiaBackendBase.Common.SkinType skinType = __instance.SkinType;
                PolytopiaSpriteRenderer house = __instance.GetHouse(tribe, __instance.HOUSE_WORKSHOP, skinType);
                house.sprite = PolyMod.Registry.GetSprite("valhalla");
                int count = __instance.plots.Count;
                int num = (int)System.Math.Floor(System.Math.Sqrt(count));

                // Put Valhalla on the tallest column so it doesnt obstruct anything with its post-rendering rendering
                int tallestplotidx = 0;
                int tallestplotamount = -1;
                for(int i=1; i<count; i++) //goes from 1 so it doesnt appear on capital
                {
                    if(__instance.plots[i].floors > tallestplotamount)
                    {
                        tallestplotamount = __instance.plots[i].floors;
                        tallestplotidx = i;
                    }
                }
                AddHouseIfNotPresent(__instance.plots[tallestplotidx], house);
            }
        }
        
        private static void AddHouseIfNotPresent(CityPlot plot, PolytopiaSpriteRenderer house)
        {
            bool flag = false;
            foreach (var h in plot.houses)
            {
                if (h.sprite == house.sprite) { flag = true; break; }
            }
            if (!flag) plot.AddHouse(house);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ActionUtils), nameof(ActionUtils.TrainUnit))]
        private static void Valhalla_Effect(GameState gameState, PlayerState playerState, TileData tile, UnitData unitData, UnitState __result)
        {
            try
            {
                if (__result == null || tile == null) return;

                if (tile != null && tile.improvement != null && tile.improvement.type == ImprovementData.Type.City)
                {
                    if (tile.improvement.HasReward(EnumCache<CityReward>.GetType("valhalla")))
                    {
                        __result.xp += 2;
                        
                        Loader.modLogger?.LogInfo($"[Conquest-City] XP successfully updated to: {__result.xp}");
                    }
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-City] Error in TrainUnit Postfix: {ex.Message}");
            }
        }

        // =========================================================================
        // C. Tax Reform
        // =========================================================================
        /*[HarmonyPostfix]
        [HarmonyPatch(typeof(TileDataExtensions), nameof(TileDataExtensions.CalculateWork), new Type[] { typeof(TileData), typeof(GameState), typeof(PlayerState), typeof(int) })]
        private static void CalculateWorkA_TaxReform(TileData tile, GameState gameState, PlayerState playerState, int improvementLevel, ref int __result)
        {
            try
            {
                if (tile == null) return;

                if (tile != null && tile.improvement != null && tile.improvement.type == ImprovementData.Type.City)
                {
                    if (tile.improvement.HasReward(EnumCache<CityReward>.GetType("taxreform")))
                    {
                        __result *= 3;
                        
                        Loader.modLogger?.LogInfo($"[Conquest-City] Work (A) successfully updated to: {__result}");
                    }
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-City] Error in CalculateWork: {ex.Message}");
            }
        }*/    

        [HarmonyPostfix]
        [HarmonyPatch(typeof(TileDataExtensions), nameof(TileDataExtensions.CalculateWork), new Type[] { typeof(TileData), typeof(GameState), typeof(int) })]
        private static void CalculateWorkB_TaxReform(TileData tile, GameState gameState, int improvementLevel, ref int __result)
        {
            try
            {
                if (tile == null) return;

                if (tile != null && tile.improvement != null && tile.improvement.type == ImprovementData.Type.City)
                {
                    if (tile.improvement.HasReward(EnumCache<CityReward>.GetType("taxreform")))
                    {
                        __result *= 3;
                        __result = Math.Min(__result, 30);
                        
                        // Loader.modLogger?.LogInfo($"[Conquest-City] Work (B) successfully updated to: {__result}");
                    }
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-City] Error in CalculateWork: {ex.Message}");
            }
        }    

        /*[HarmonyPostfix]
        [HarmonyPatch(typeof(CityStatusNameContainer), nameof(CityStatusNameContainer.SetCity))]
        private static void SetCity_ChangeWorkIcon(CityStatusNameContainer __instance, global:: City city)
        {
            if (__instance.workContainer != null && __instance.workContainer.gameObject.activeSelf && __instance.workIcon != null)
            {
                __instance.workIcon.sprite = PolyMod.Registry.GetSprite("taxreform"); 

                __instance.UpdateSize();
            }
        }*/ 

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CommandUtils), nameof(CommandUtils.GetTrainableUnits))]
        private static void DenyTrainableUnits_TaxReform(GameState gameState, PlayerState player, TileData tile, ref Il2CppSystem.Collections.Generic.List<TrainCommand> __result, bool includeUnavailable = false)
        {
            if (tile.owner != player.Id)
            {
                return;
            }

            if (!tile.improvement.HasReward(EnumCache<CityReward>.GetType("taxreform")))
            {
                return;
            }

            __result = new Il2CppSystem.Collections.Generic.List<TrainCommand>();
            return;
        }

        // =========================================================================
        // D. Reactions
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(CityRewardReaction), nameof(CityRewardReaction.Execute))]
        public static void CityRewardReaction_Postfix(CityRewardReaction __instance, Il2CppSystem.Action onComplete)
        {
            try
            {
                if (GameManager.PreliminaryGameSettings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                    && GameManager.PreliminaryGameSettings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
                {
                    return;
                }

                PlayerState playerState;
                GameManager.GameState.TryGetPlayer(__instance.action.PlayerId, out playerState);

                Loader.modLogger?.LogInfo("[Conquest-City] CityRewardReaction Postfix processing visuals.");

                TileData tile = GameManager.GameState.Map.GetTile(__instance.action.Coordinates);
                if (tile == null) return;

                Tile instance = tile.GetInstance();

                Il2CppReferenceArray<TileData> areaSorted = GameManager.GameState.Map.GetAreaSorted(tile.coordinates, 3, true, true);
                if (areaSorted != null)
                {
                    for (int i = areaSorted.Count - 1; i >= 0; i--)
                    {
                        Tile instance2 = areaSorted[i].GetInstance();
                        if (instance2 != null)
                        {
                            instance2.Render();
                        }
                    }
                }

                if (!GameManager.Client.IsReplay)
                {
                    InputEvents.SelectionCleared();
                    ResourceManager.IncomeChanged(__instance.action.PlayerId);
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-City] Error in CityRewardReaction: {ex}");
            }
        }
    }
}