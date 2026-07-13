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
        public static bool ShowCommandTrigger_Prefix(CommandTrigger commandTrigger)
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
        public static void GetCityRewardsForLevel_Postfix(ref Il2CppStructArray<CityReward> __result, ImprovementData data, int level)
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
                //2 => EnumCache<CityReward>.GetType("two"),
                //3 => EnumCache<CityReward>.GetType("three"),
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

        public static void Populate(GameState gameState, TileData tile, int FruitsToSpawn)
        {
            Il2CppSystem.Collections.Generic.List<TileData> cityAreaSorted = ActionUtils.GetCityAreaSorted(gameState, tile);
            cityAreaSorted.Reverse();
            int num = FruitsToSpawn;
            for (int i = 0; i < cityAreaSorted.Count; i++)
            {
                if (cityAreaSorted[i].terrain == Polytopia.Data.TerrainData.Type.Field && cityAreaSorted[i].resource == null && cityAreaSorted[i].improvement == null && num > 0)
                {
                    Tile tileInstance = MapRenderer.Current.GetTileInstance(tile.coordinates);
                    tileInstance.SpawnSparkles();
                    gameState.ActionStack.Add((ActionBase)new BuildAction(tile.owner, EnumCache<ImprovementData.Type>.GetType("createfruit"), cityAreaSorted[i].coordinates, deductCost: false));
                    num--;
                }
            }
            gameState.ActionStack.Add((ActionBase)new IncreaseCurrencyAction(tile.owner, tile.coordinates, num, 0));
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

        [HarmonyPostfix]
        [HarmonyPatch(typeof(AI), nameof(AI.ChooseCityReward))]
        private static void AI_Choose(GameState gameState, TileData tile, CityReward[] rewards, ref CityReward __result)
        {
            
            GameLogicData gld = gameState.GameLogicData;
            CityReward[] rewardarray = GetRewardsForLevel(gld.GetImprovementData(tile.improvement.type), tile.improvement.level - 1);

            System.Random random = new System.Random();

            PlayerState playerState;
            if (!gameState.TryGetPlayer(tile.owner, out playerState) || !GameManager.GameState.GameLogicData.TryGetData(playerState.tribe, out TribeData tribeData))
            {
                return;
            }
            if (tile.improvement.level == 2)
            {
                int num = random.Next(0, rewardarray.Length - 1);
                __result = rewardarray[num];
            }
            else
            if (tile.improvement.level >= 5)
            {
                int num = 1;
                __result = rewardarray[num];
            }
            else
            {
                int num = random.Next(0, rewardarray.Length);
                __result = rewardarray[num];
            }

            /*Main.modLogger.LogMessage("AI chose reward: "+rewardarray[num]);
            if(int.TryParse(rewardarray[num].ToString(), out int value))
            {
                modLogger.LogMessage("\n\nDING_Postfix\n");
                modLogger.LogMessage(tile.coordinates);
                gameState.TryGetPlayer(tile.owner, out PlayerState player);
                modLogger.LogMessage(player.UserName);
            }*/
        }

        public static CityReward[] GetRewardsForLevel(ImprovementData data, int level)
        {
            return data.GetCityRewardsForLevel(level);
        }

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