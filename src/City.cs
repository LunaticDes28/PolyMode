using System;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSystem.Collections.Generic;
using PolyMod;
using Polytopia.Data;
using PolytopiaBackendBase.Game;
using PolytopiaBackendBase.Common;
using UnityEngine;

namespace PolyMode
{
    public static class City
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(CommandTriggerUIUtils), nameof(CommandTriggerUIUtils.ShowCommandTrigger))]
        public static bool ShowCommandTrigger_ThirdOption(CommandTrigger commandTrigger)
        {
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
            if (tile.capitalOf != 0)
            {
                if (tile.improvement.level == 2)
                {
                    CityReward[] cityRewardsForLevel = new CityReward[]
                    {
                        CityRewardData.cityRewards[0],
                        CityRewardData.cityRewards[1],
                    };
                    rewardPopup.SetData(playerState, tile, cityRewardsForLevel, RewardPopup.PopupType.CityLevelUp, false);
                    rewardPopup.Show();
                    AudioManager.PlaySFX(SFXTypes.RewardStart, 0, 1f, 1f, 0f);
                    return false;
                }
                else
                {
                    CityReward[] cityRewardsForLevel = global::ImprovementDataExtensions.GetCityRewardsForLevel(improvementData, (int)(tile.improvement.level - 1));
                    rewardPopup.SetData(playerState, tile, cityRewardsForLevel, RewardPopup.PopupType.CityLevelUp, false);
                    rewardPopup.Show();
                    AudioManager.PlaySFX(SFXTypes.RewardStart, 0, 1f, 1f, 0f);
                    return false;
                }
            }
            else
            {
                CityReward[] cityRewardsForLevel = global::ImprovementDataExtensions.GetCityRewardsForLevel(improvementData, (int)(tile.improvement.level - 1));
                rewardPopup.SetData(playerState, tile, cityRewardsForLevel, RewardPopup.PopupType.CityLevelUp, false);
                rewardPopup.Show();
                AudioManager.PlaySFX(SFXTypes.RewardStart, 0, 1f, 1f, 0f);
                return false;
            }       
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ImprovementDataExtensions), nameof(ImprovementDataExtensions.GetCityRewardsForLevel))]
        public static void GetCityRewardsForLevel_ThirdOption(ref Il2CppStructArray<CityReward> __result, ImprovementData data, int level)
        {
            if (GameManager.PreliminaryGameSettings.RulesGameMode != EnumCache<GameMode>.GetType("conquest")
                && GameManager.PreliminaryGameSettings.RulesGameMode != EnumCache<GameMode>.GetType("reign"))
            {
                return;
            }
            int num = Math.Min(level - 1, (CityRewardData.cityRewards.Length / 2) - 1) * 2;
            CityReward customReward = level switch
            {
                1 => EnumCache<CityReward>.GetType("one"),
                2 => EnumCache<CityReward>.GetType("two"),
                3 => EnumCache<CityReward>.GetType("three"),
                //4 => EnumCache<CityReward>.GetType("four"),
                _ => CityReward.None
            };

            CityReward[] newRewards = new CityReward[]
            {
                CityRewardData.cityRewards[num],
                CityRewardData.cityRewards[num + 1],
                customReward
            };

            __result = newRewards;
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
        [HarmonyPatch(typeof(UIIconData), "GetSprite")]
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

            if (__instance.Reward == EnumCache<CityReward>.GetType("one"))
            {
                Main.DestroyCityConquest(state, tile, playerState, true);
            }
            else
            {
                if (__instance.Reward == EnumCache<CityReward>.GetType("two"))
                {
                    return;
                }
                if (__instance.Reward == EnumCache<CityReward>.GetType("three"))
                {
                    return;
                }
                else if (__instance.Reward == EnumCache<CityReward>.GetType("four"))
                {
                    return;
                }
            }
        }

        public static CityReward[] GetRewardsForLevel(ImprovementData data, int level)
        {
            return data.GetCityRewardsForLevel(level);
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
            bool hasTwo = a.improvement.HasReward(EnumCache<CityReward>.GetType("two"));

            if (hasTwo)
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
                    if (tile.improvement.HasReward(EnumCache<CityReward>.GetType("two")))
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
                    if (tile.improvement.HasReward(EnumCache<CityReward>.GetType("three")))
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
                    if (tile.improvement.HasReward(EnumCache<CityReward>.GetType("three")))
                    {
                        __result *= 3;
                        
                        Loader.modLogger?.LogInfo($"[Conquest-City] Work (B) successfully updated to: {__result}");
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
                __instance.workIcon.sprite = PolyMod.Registry.GetSprite("three"); 

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

            if (!tile.improvement.HasReward(EnumCache<CityReward>.GetType("three")))
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

                Loader.modLogger?.LogInfo("[Conquest-City] CityRewardReaction Postfix processing visuals.");

                TileData tile = GameManager.GameState.Map.GetTile(__instance.action.Coordinates);
                if (tile == null) return;

                Tile instance = tile.GetInstance();

                /*if (instance != null)
                {
                    ReactionUtils.UpdateSurroundingBordersAndTransportPaths(__instance.action.PlayerId, tile);
                }*/

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