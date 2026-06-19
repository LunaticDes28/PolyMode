using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using Polytopia.Data;
using PolytopiaBackendBase.Common;
using UnityEngine.EventSystems;
using PolytopiaBackendBase.Game;

namespace Polyquest
{
    public static class Main
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameRules), nameof(GameRules.LoadPreset))]
        private static void GameRules_LoadPreset_Postfix(GameRules __instance, GameMode gameMode)
        {
            if (gameMode == EnumCache<GameMode>.GetType("conquest") || (int)gameMode == 8)
            {
                __instance.AllowMirrorPick = false;
                __instance.AllowTechSharing = false;
                __instance.AllowSpecialTribes = true;
                __instance.ScoreLimit = 0;
                __instance.TurnLimit = 0;
                __instance.WinByCapital = false;
                __instance.WinByExtermination = true;
                __instance.PlayerDeathCondition = GameRules.DeathCondition.Cities;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreen), nameof(GameSetupScreen.Show))]
        private static void RegisterConquestEnum()
        {
            EnumCache<GameMode>.GetType("conquest");
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateInternal))]
        private static void ConquestCityDistribution(MapGenerator __instance, GameState gameState, MapGeneratorSettings settings)
        {
            bool isConquest = gameState.Settings.RulesGameMode == EnumCache<GameMode>.GetType("conquest") || 
                            gameState.Settings.BaseGameMode == EnumCache<GameMode>.GetType("conquest");

            if (!isConquest) return;

            Log.Info("{0} Conquest mode active", "<color=#639ad8>[MapGenerator]</color>");

            int desiredCities = (int)(gameState.Map.Tiles.Length * 0.085f);

            Il2CppSystem.Collections.Generic.List<int> cities = new Il2CppSystem.Collections.Generic.List<int>();

            __instance.GeneratePreTerrainCities(gameState.Map, cities, desiredCities / 2);
            __instance.AddPostTerrainCities(gameState.Map, desiredCities - cities.Count);

            DistributeConquestCities(__instance, gameState.Map, gameState);
        }

        private static void DistributeConquestCities(MapGenerator gen, MapData map, GameState state)
        {
            List<int> cityList = new List<int>();

            for (int i = 0; i < map.Tiles.Length; i++)
            {
                if (map.Tiles[i].HasImprovement(ImprovementData.Type.City))
                    cityList.Add(i);
            }

            ShuffleList(cityList);
            int playerCount = state.PlayerCount;

            for (int i = 0; i < cityList.Count; i++)
            {
                TileData tile = map.Tiles[cityList[i]];
                PlayerState player = state.PlayerStates[i % playerCount];
                SetAsConquestCity(tile, player);
            }
        }

        private static void ShuffleList(List<int> list, System.Random random = null)
        {
            if (random == null) random = new System.Random();
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private static void SetAsConquestCity(TileData tile, PlayerState player)
        {
            tile.owner = player.Id;
            tile.improvement = new ImprovementState
            {
                type = ImprovementData.Type.City,
                level = 1,
                borderSize = 2,
                production = 1,
                founded = 0
            };
            tile.capitalOf = 0;
            player.cities++;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameLogicData), nameof(GameLogicData.GetTechPrice))]
        private static void Conquest_TechCost_Postfix(GameLogicData __instance, TechData techData, PlayerState playerState, GameState state, ref int __result)
        {
            if (state == null || techData == null) return;

            bool isConquest = state.Settings.RulesGameMode == EnumCache<GameMode>.GetType("conquest") || 
                            state.Settings.BaseGameMode == EnumCache<GameMode>.GetType("conquest");

            if (!isConquest) return;

            // Tech becomes more expensive over time in Conquest mode
            int addition = (int)state.CurrentTurn;   // +1 per turn
            addition = Math.Min(addition, techData.cost * 5);    // Cap at 5x

            __result = (int)Math.Ceiling((double)(__result + addition));
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CaptureCityAction), nameof(CaptureCityAction.ExecuteDefault))]   // Change if method name is different
        private static bool Conquest_CaptureCityAction_Prefix(CaptureCityAction __instance, GameState gameState)
        {
            if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("conquest"))
            {
                return true;
            } 

		TileData cityTile = gameState.Map.GetTile(__instance.Coordinates);
		PlayerState playerState;
		gameState.TryGetPlayer(__instance.PlayerId, out playerState);
            DestroyCityConquest(gameState, cityTile, playerState);
            return false;
        }

        private static void DestroyCityConquest(GameState gameState, TileData cityTile, PlayerState attacker)
        {
            if (cityTile?.improvement?.type != ImprovementData.Type.City) return;

            // Reward
            int reward = cityTile.improvement.level * 2 + (int)gameState.CurrentTurn;
            attacker.Currency = attacker.Currency + reward;
            bool leaveRuin = UnityEngine.Random.value < 0.6f;

            // Leave ruin sometimes
            if (leaveRuin)
            {
                cityTile.improvement = new ImprovementState 
                { 
                    type = ImprovementData.Type.Ruin, 
                    level = 1 
                };
            }
            else
            {
                cityTile.improvement = null;
            }

            cityTile.owner = 0;
            cityTile.capitalOf = 0;

            Log.Info($"[Conquest] City destroyed by player {attacker.Id} (+{reward} stars)");
        }
    }
}