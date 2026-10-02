using System;
using System.Collections.Generic;
using HarmonyLib;
using Polytopia.Data;
using PolytopiaBackendBase.Game;
using PolyMode;

namespace Rush
{
    public static class AI_2
    {
        // =====================================================================
        // Diplomacy / progress (rusha/rushb)
        // =====================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(AI), nameof(AI.RateBattle))]
        private static void RateBattle_Cities(
            GameState gameState, UnitState attackingUnit, TileData defendingTile, ref float __result)
        {
            if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("rushb")) return;
            if (defendingTile.owner == 0) return;

            float advantageFactor = (float)Math.Pow(
                gameState.CurrentTurn
                / Math.MaxMagnitude(1, gameState.Settings.rules.TurnLimit - 1),
                5);
            if (advantageFactor > 0f)
                __result += advantageFactor;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(OpinionManager), nameof(OpinionManager.UpdateOpinion))]
        private static void UpdateOpinion_Cities(
            OpinionManager __instance, GameState gameState, PlayerState player, PlayerState opponent)
        {
            if (gameState.Settings.RulesGameMode != EnumCache<GameMode>.GetType("rushb")) return;

            if (player == opponent || player.Id == 255 || opponent.Id == 255
                || !opponent.IsAlive(gameState))
                return;

            if (player.GetRelation(opponent.Id).FirstMeet < 0
                && opponent.GetRelation(player.Id).LastAttackTurn < 0)
                return;

            float advantageFactor = (float)Math.Pow(
                gameState.CurrentTurn
                / Math.MaxMagnitude(1, gameState.Settings.rules.TurnLimit - 1),
                3);

            var opinionState = new OpinionState();
            if (advantageFactor > 0f)
                opinionState.AddOpinion(advantageFactor * 1.5f, OpinionManager.Type.Winning);

            if (!__instance.Opinions.ContainsKey(opponent.Id))
                __instance.Opinions[opponent.Id] = new OpinionState();

            __instance.Opinions[opponent.Id].AddOpinion(
                opinionState.GetOpinion(OpinionManager.Type.Winning) * -1f,
                OpinionManager.Type.Winning);
        }

        // =====================================================================
        // Tech goal (rushc)
        // =====================================================================
        public static readonly Dictionary<long, int> TechLastGrantAge =
            new Dictionary<long, int>();

        static int _lastTechTickTurn = -1;

        public static int TechCount(PlayerState p)
            => p?.availableTech?.Count ?? 0;

        public static bool HasAllTech(GameState state, PlayerState p)
        {
            if (p == null || state?.GameLogicData == null) return false;
            var all = state.GameLogicData.GetAllTechForTribe(p.tribe);
            if (all == null || p.availableTech == null) return false;
            return p.availableTech.Count >= all.Count;
        }

        static long TechCityKey(WorldCoordinates c)
            => ((long)c.X << 32) ^ (uint)c.Y;

        public static int CountMonumentsInCity(GameState state, TileData cityTile)
        {
            if (cityTile?.improvement == null
                || cityTile.improvement.type != ImprovementData.Type.City
                || state?.Map == null)
                return 0;

            int radius = Math.Max(1, (int)cityTile.improvement.borderSize);
            var area = state.Map.GetArea(cityTile.coordinates, radius, true, false);
            int n = 0;
            foreach (var t in area)
            {
                if (t.rulingCityCoordinates != cityTile.coordinates) continue;
                if (t.improvement == null) continue;
                try
                {
                    if (t.improvement.type.IsMonument()) n++;
                }
                catch
                {
                    var ty = t.improvement.type;
                    if (ty >= ImprovementData.Type.Monument1 && ty <= ImprovementData.Type.Monument7)
                        n++;
                }
            }
            return n;
        }

        /// <summary>borderSize ≥2 → 2, else 3.</summary>
        public static int TechInterval(ImprovementState city)
        {
            if (city == null) return 3;
            return city.borderSize >= 2 ? 2 : 3;
        }

        /// <summary>
        /// Base interval + (monuments - 1) if more than one monument in the city.
        /// </summary>
        public static int TechIntervalForCity(GameState state, TileData cityTile)
        {
            int interval = TechInterval(cityTile.improvement);
            int monuments = CountMonumentsInCity(state, cityTile);
            if (monuments > 1)
                interval += monuments - 1;
            return Math.Max(1, interval);
        }

        public static bool CityHasMonument(GameState state, TileData cityTile)
        {
            if (cityTile?.improvement == null
                || cityTile.improvement.type != ImprovementData.Type.City)
                return false;

            int radius = Math.Max(1, (int)cityTile.improvement.borderSize);
            var area = state.Map.GetArea(cityTile.coordinates, radius, true, false);
            foreach (var t in area)
            {
                if (t.rulingCityCoordinates != cityTile.coordinates) continue;
                if (t.improvement == null) continue;
                try
                {
                    if (t.improvement.type.IsMonument())
                        return true;
                }
                catch
                {
                    var ty = t.improvement.type;
                    if (ty >= ImprovementData.Type.Monument1
                        && ty <= ImprovementData.Type.Monument7)
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Grant up to <paramref name="need"/> cheapest unlockable techs.
        /// Re-queries unlockables each time so multiple monuments can unlock different techs.
        /// </summary>
        public static bool TryGrantTech(GameState state, PlayerState player, int need)
        {
            if (state?.GameLogicData == null || player == null || need <= 0)
                return false;

            var taken = new HashSet<TechData.Type>();
            if (player.availableTech != null)
            {
                foreach (var t in player.availableTech)
                    taken.Add(t);
            }

            int granted = 0;
            for (int n = 0; n < need; n++)
            {
                var list = state.GameLogicData.GetUnlockableTech(player, state);
                if (list == null || list.Count == 0)
                    break;

                TechData? best = null;
                for (int i = 0; i < list.Count; i++)
                {
                    var t = list[i];
                    if (t == null) continue;
                    if (taken.Contains(t.type)) continue;
                    if (best == null || t.cost < best.cost)
                        best = t;
                }

                if (best == null)
                    break;

                taken.Add(best.type);
                state.ActionStack.Add(new ResearchAction(player.Id, best.type, 0));
                granted++;
            }

            return granted > 0;
        }

        public static void TickTechResearch(GameState state)
        {
            if (state?.Settings == null || state.Map?.Tiles == null) return;
            if (state.Settings.RulesGameMode != EnumCache<GameMode>.GetType("rushc"))
                return;

            var grantsThisTick = new Dictionary<byte, int>();

            foreach (var tile in state.Map.Tiles)
            {
                if (tile.improvement == null) continue;
                if (!tile.improvement.type.IsMonument()) continue;
                if (tile.owner == 0 || tile.owner == 255) continue;

                var cityCoords = tile.rulingCityCoordinates;
                if (cityCoords == WorldCoordinates.NULL_COORDINATES) continue;

                TileData city = state.Map.GetTile(cityCoords);
                if (city?.improvement == null) continue;

                if (IsCityResearchBlocked(state, city)) continue;

                if (!state.TryGetPlayer(tile.owner, out var player)) continue;
                if (!player.IsAlive(state)) continue;

                // IMPORTANT: interval from CITY, age from MONUMENT
                int interval = TechIntervalForCity(state, city);
                int age = tile.improvement.GetAge(state);
                if (age <= 0 || age % interval != 0) continue;

                long k = TechCityKey(tile.coordinates);
                if (TechLastGrantAge.TryGetValue(k, out int last) && last == age)
                    continue;

                TechLastGrantAge[k] = age;

                byte id = player.Id;
                if (!grantsThisTick.ContainsKey(id))
                    grantsThisTick[id] = 0;
                grantsThisTick[id]++;
            }

            foreach (var kv in grantsThisTick)
            {
                if (!state.TryGetPlayer(kv.Key, out var player)) continue;
                TryGrantTech(state, player, kv.Value);
            }
        }

        /// <summary>
        /// Blocked if enemy unit in city territory, or certain mega units (any owner).
        /// Friendly normal units do not block.
        /// </summary>
        public static bool IsCityResearchBlocked(GameState state, TileData cityTile)
        {
            if (cityTile?.improvement == null
                || cityTile.improvement.type != ImprovementData.Type.City)
                return true;

            int radius = Math.Max(1, (int)cityTile.improvement.borderSize);
            var area = state.Map.GetArea(cityTile.coordinates, radius, true, true);
            foreach (var t in area)
            {
                if (t.rulingCityCoordinates != cityTile.coordinates) continue;
                if (t.unit == null) continue;

                PlayerState player;
                state.TryGetPlayer(t.unit.owner, out player);
                if (t.unit.owner != cityTile.owner && !player.HasPeaceWith(cityTile.owner))
                    return true;

                var ut = t.unit.type;
                if (ut == UnitData.Type.Giant
                    || ut == UnitData.Type.BabyDragon
                    || ut == UnitData.Type.FireDragon
                    || ut == UnitData.Type.Gaami
                    || ut == UnitData.Type.Crab
                    || ut == UnitData.Type.Hexapod
                    || ut == UnitData.Type.Segment)
                    return true;
            }
            return false;
        }

        /// <returns>Turns until next grant. blocked from city territory rules.</returns>
        public static int GetResearchTurnsDisplay(
            TileData monumentTile, GameState state, out bool blocked)
        {
            blocked = true;
            if (monumentTile == null || state?.Map == null) return 0;

            var cityCoords = monumentTile.rulingCityCoordinates;
            if (cityCoords == WorldCoordinates.NULL_COORDINATES) return 0;

            TileData city = state.Map.GetTile(cityCoords);
            if (city?.improvement == null) return 0;

            blocked = IsCityResearchBlocked(state, city);

            int interval = TechIntervalForCity(state, city);
            int age = monumentTile.improvement.GetAge(state);
            int mod = age % interval;
            return mod == 0 ? interval : interval - mod;
        }

        public static void ClearTechSession()
        {
            _lastTechTickTurn = -1;
            TechLastGrantAge.Clear();
        }

        public static void TryTickOnTurnAdvance(GameState state)
        {
            if (state == null) return;
            int turn = (int)state.CurrentTurn;
            if (turn == _lastTechTickTurn) return;
            _lastTechTickTurn = turn;
            TickTechResearch(state);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameState), nameof(GameState.EndPlayerTurn))]
        static void EndPlayerTurn_TechTick(GameState __instance, bool newTurn)
        {
            try { TryTickOnTurnAdvance(__instance); }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Rush-AI] TechTick: {ex.Message}");
            }
        }
    }
}