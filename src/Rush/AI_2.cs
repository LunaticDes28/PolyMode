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

        /// <summary>Cached UI digit per monument (frozen until owner start/end commits).</summary>
        public static readonly Dictionary<long, int> DisplayedTurns =
            new Dictionary<long, int>();

        public static readonly Dictionary<long, bool> DisplayedBlocked =
            new Dictionary<long, bool>();

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

        public static int TechInterval(ImprovementState city)
        {
            if (city == null) return 3;
            return city.borderSize >= 2 ? 2 : 3;
        }

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

        static bool IsMonumentTile(TileData tile)
        {
            if (tile?.improvement == null) return false;
            try { return tile.improvement.type.IsMonument(); }
            catch
            {
                var ty = tile.improvement.type;
                return ty >= ImprovementData.Type.Monument1
                    && ty <= ImprovementData.Type.Monument7;
            }
        }

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

        public static void TickTechResearchForPlayer(GameState state, PlayerState player)
        {
            if (state?.Settings == null || state.Map?.Tiles == null || player == null)
                return;
            if (state.Settings.RulesGameMode != EnumCache<GameMode>.GetType("rushc"))
                return;
            if (!player.IsAlive(state)) return;

            int need = 0;

            foreach (var tile in state.Map.Tiles)
            {
                if (!IsMonumentTile(tile)) continue;
                if (tile.owner != player.Id) continue;

                var cityCoords = tile.rulingCityCoordinates;
                if (cityCoords == WorldCoordinates.NULL_COORDINATES) continue;

                TileData city = state.Map.GetTile(cityCoords);
                if (city?.improvement == null) continue;
                if (IsCityResearchBlocked(state, city)) continue;

                int interval = TechIntervalForCity(state, city);
                int age = tile.improvement.GetAge(state);
                if (age <= 0 || interval <= 0 || age % interval != 0) continue;

                long k = TechCityKey(tile.coordinates);
                if (TechLastGrantAge.TryGetValue(k, out int last) && last == age)
                    continue;

                TechLastGrantAge[k] = age;
                need++;
            }

            if (need > 0)
                TryGrantTech(state, player, need);

            CommitDisplayForPlayer(state, player.Id);

            try { OverlayPatches.RefreshMonumentOverlaysForPlayer(player.Id); }
            catch { }
        }

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

                if (t.unit.owner != cityTile.owner)
                {
                    if (!state.TryGetPlayer(t.unit.owner, out var unitOwner)
                        || !unitOwner.HasPeaceWith(cityTile.owner))
                        return true;
                }

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

        /// <summary>Raw countdown from age (no cache).</summary>
        public static int ComputeResearchTurns(
            TileData monumentTile, GameState state, out bool blocked)
        {
            blocked = true;
            if (monumentTile?.improvement == null || state?.Map == null) return 0;

            var cityCoords = monumentTile.rulingCityCoordinates;
            if (cityCoords == WorldCoordinates.NULL_COORDINATES) return 0;

            TileData city = state.Map.GetTile(cityCoords);
            if (city?.improvement == null) return 0;

            blocked = IsCityResearchBlocked(state, city);

            int interval = TechIntervalForCity(state, city);
            int age = monumentTile.improvement.GetAge(state);
            if (interval <= 0) return 0;
            if (age <= 0) return interval;

            int mod = age % interval;
            if (mod == 0) return interval;
            return interval - mod;
        }

        /// <summary>
        /// Recompute and cache digit + blocked for all monuments owned by playerId.
        /// Call on that player's StartTurn / EndTurn only.
        /// </summary>
        public static void CommitDisplayForPlayer(GameState state, byte playerId)
        {
            if (state?.Map?.Tiles == null) return;
            if (playerId == 0 || playerId == 255) return;

            foreach (var tile in state.Map.Tiles)
            {
                if (!IsMonumentTile(tile)) continue;
                if (tile.owner != playerId) continue;

                long k = TechCityKey(tile.coordinates);
                bool blocked;
                int turns = ComputeResearchTurns(tile, state, out blocked);
                DisplayedTurns[k] = turns;
                DisplayedBlocked[k] = blocked;
            }
        }

        /// <summary>
        /// UI path. refreshBlockedOnly: move/train — update (O)/(X), keep cached digit.
        /// </summary>
        public static int GetResearchTurnsDisplay(
            TileData monumentTile, GameState state, out bool blocked)
        {
            return GetResearchTurnsDisplay(monumentTile, state, false, out blocked);
        }

        public static int GetResearchTurnsDisplay(
            TileData monumentTile, GameState state, bool refreshBlockedOnly, out bool blocked)
        {
            blocked = true;
            if (monumentTile?.improvement == null || state?.Map == null) return 0;

            long k = TechCityKey(monumentTile.coordinates);

            if (refreshBlockedOnly)
            {
                var cityCoords = monumentTile.rulingCityCoordinates;
                if (cityCoords != WorldCoordinates.NULL_COORDINATES)
                {
                    TileData city = state.Map.GetTile(cityCoords);
                    if (city?.improvement != null)
                        blocked = IsCityResearchBlocked(state, city);
                }
                DisplayedBlocked[k] = blocked;

                if (DisplayedTurns.TryGetValue(k, out int cached))
                    return cached;

                int t = ComputeResearchTurns(monumentTile, state, out blocked);
                DisplayedTurns[k] = t;
                DisplayedBlocked[k] = blocked;
                return t;
            }

            if (DisplayedTurns.TryGetValue(k, out int turns)
                && DisplayedBlocked.TryGetValue(k, out blocked))
                return turns;

            turns = ComputeResearchTurns(monumentTile, state, out blocked);
            DisplayedTurns[k] = turns;
            DisplayedBlocked[k] = blocked;
            return turns;
        }

        /// <summary>
        /// Call when a match ends or a new level loads so the next game
        /// in the same process does not reuse grant/display state.
        /// Process exit clears memory automatically.
        /// </summary>
        public static void ClearTechSession()
        {
            TechLastGrantAge.Clear();
            DisplayedTurns.Clear();
            DisplayedBlocked.Clear();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(StartTurnAction), nameof(StartTurnAction.ExecuteDefault))]
        static void StartTurnAction_ExecuteDefault_TechTick(
            StartTurnAction __instance, GameState gameState)
        {
            try
            {
                if (gameState == null || __instance == null) return;
                if (!gameState.TryGetPlayer(__instance.PlayerId, out var player)) return;
                if (player.Id == 0 || player.Id == 255) return;

                TickTechResearchForPlayer(gameState, player);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Rush-AI] StartTurn TechTick: {ex.Message}");
            }
        }

        // Clear when a level loads so a new match never inherits old caches
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameManager), nameof(GameManager.LoadLevel))]
        static void LoadLevel_ClearTechSession()
        {
            try { ClearTechSession(); }
            catch { }
        }
    }
}