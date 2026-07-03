using HarmonyLib;
using PolytopiaBackendBase.Game;
using Polytopia.Data;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace PolyMode
{
    public static class Main
    {
        // =========================================================================
        // A. GameMode Settings
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameStateUtils), nameof(GameStateUtils.GenerateMap))]
        private static void GenerateMap_Postfix(GameState gameState)
        {
            if (gameState?.Settings == null) return;

            try
            {
                // If player generate new game without mode re-selection, this function will be skipped
                // It is because bool flag is disabled after map generation, and only applied when mode is re-selected 
                bool isConquest = UI_2.IsConquestSelected;
                if (!isConquest) return;

                Loader.modLogger?.LogInfo("[Conquest-Map] Conquest Mode selected!");

                // Pseudo GameSettings in GameState
                int registeredConquestId = PolyMod.Registry.gameModesAutoidx - 1;
                gameState.Settings.RulesGameMode = (GameMode)registeredConquestId;
                gameState.Settings.rules.WinByExtermination = true;
                
                // Disable bool flag after GameMode initialized
                UI_2.IsConquestSelected = false;

                Loader.modLogger?.LogInfo($"[Conquest-Map] RulesGameMode stamped as ID: {registeredConquestId}");
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Map] GameStateUtils error: {ex.Message}");
            }
        }

        // =========================================================================
        // B. Village Generation + Logics
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateInternal))]
        private static void GenerateInternal_Postfix(MapGenerator __instance, GameState gameState)
        {
            try
            {
                int registeredConquestId = PolyMod.Registry.gameModesAutoidx - 1;
                if ((int)gameState.Settings.RulesGameMode != registeredConquestId) return;

                Loader.modLogger?.LogInfo($"[Conquest-Map] ConquestVillageGeneration...");
                ConquestVillageGeneration(__instance, gameState);

            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Map] MapGenerator error: {ex.Message}");
            }
        }        
        
        private static void ConquestVillageGeneration(MapGenerator gen, GameState gameState)
        {
            try
            {
                List<TileData> neutralVillages = new List<TileData>();
                for (int i = 0; i < gameState.Map.Tiles.Length; i++)
                {
                    TileData tile = gameState.Map.Tiles[i];
                    if (tile.HasImprovement(ImprovementData.Type.City) && tile.owner == 0)
                    {
                        neutralVillages.Add(tile);
                    }
                }

                int playerCount = gameState.PlayerCount;
                if (playerCount <= 0) return;
                Loader.modLogger!.LogInfo($"[Conquest-Map] {neutralVillages.Count} villages after vanilla generation for {playerCount} players.");

                int remainder = neutralVillages.Count % playerCount;
                int citiesToSpawn = playerCount - remainder;
                Loader.modLogger!.LogInfo($"[Conquest-Map] {citiesToSpawn} more villages needed to even out distribution for all players.");

                // Tries to add villages if it is close to distribute 1 more villages to each player
                if (remainder > 0 && remainder >= (playerCount * 0.5f))
                {
                    Loader.modLogger!.LogInfo($"[Conquest-Map] Trying to add villages!");
   
                    for (int s = 0; s < citiesToSpawn; s++)
                    {
                        Loader.modLogger!.LogInfo($"[Conquest-Map] Attempting to spawn new village...");

                        WorldCoordinates emergencyCoords = gen.GetEmergencyCityPosition(gameState, gameState.Map);
                        if (emergencyCoords != WorldCoordinates.NULL_COORDINATES)
                        {
                            int tileIndex = gameState.Map.GetTileIndex(emergencyCoords);
                            TileData targetTile = gameState.Map.Tiles[tileIndex];
                            gen.SetTileAsCity(targetTile);
                            neutralVillages.Add(targetTile);
                            Loader.modLogger!.LogInfo($"[Conquest-Map] {s+1}st emergency village placed at {emergencyCoords}.");
                        }
                        else
                        {
                            Loader.modLogger!.LogInfo($"[Conquest-Map] Failure to spawn new village!");
                            break;
                        }
                    }
                    
                    Loader.modLogger!.LogInfo($"[Conquest-Map] {neutralVillages.Count} villages after custom generation for {playerCount} players.");
                }

                // Decide which village to scrap based on proximity (if necessary)
                int maxCitiesPerPlayer = neutralVillages.Count / playerCount;
                HashSet<WorldCoordinates> assignedCoordinates = new HashSet<WorldCoordinates>();

                for (int round = 0; round < maxCitiesPerPlayer; round++)
                {
                    for (int p = 0; p < playerCount; p++)
                    {
                        PlayerState player = gameState.PlayerStates[p];
                        AssignClosestVillage(neutralVillages, assignedCoordinates, player);
                    }
                }

                // Convert excess to ruins
                int ruinsCount = 0;
                for (int i = neutralVillages.Count - 1; i >= 0; i--)
                {
                    var village = neutralVillages[i];
                    if (!assignedCoordinates.Contains(village.coordinates))
                    {
                        village.improvement = new ImprovementState
                        {
                            type = ImprovementData.Type.Ruin,
                            borderSize = 0,
                            level = 1,
                            production = 1,
                            founded = 0
                        };
                        neutralVillages.RemoveAt(i);
                        ruinsCount++;
                    }
                }

                Loader.modLogger!.LogInfo($"[Conquest-Map] ConquestVillageGeneration complete. Converted {ruinsCount} villages to ruins. {neutralVillages.Count} villages remain.");
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Map] ConquestVillageGeneration failed: {ex.Message}");
            }
        }

        // =========================================================================
        // C. City Distribution
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(StartMatchAction), nameof(StartMatchAction.ExecuteDefault))]
        private static void StartMatchAction_ExecuteDefault_Postfix(StartMatchAction __instance, GameState gameState)
        {
            if (gameState?.Settings == null) return;
            try
            {
                int registeredConquestId = PolyMod.Registry.gameModesAutoidx - 1;
                if ((int)gameState.Settings.RulesGameMode != registeredConquestId) return;

                Loader.modLogger?.LogInfo("[Conquest-Match] Executing village initialization in StartMatchAction...");
                ConquestVillageDistribution(gameState);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Match] Critical failure in StartMatchAction: {ex.Message}");
            }
        }

        private static void ConquestVillageDistribution(GameState gameState)
        {
            List<TileData> neutralVillages = new List<TileData>();
            for (int i = 0; i < gameState.Map.Tiles.Length; i++)
            {
                TileData tile = gameState.Map.Tiles[i];
                if (tile.HasImprovement(ImprovementData.Type.City) && tile.owner == 0)
                {
                    neutralVillages.Add(tile);
                }
            }

            int playerCount = gameState.PlayerCount;
            if (playerCount == 0) return;

            int maxCitiesPerPlayer = neutralVillages.Count / playerCount;
            HashSet<WorldCoordinates> assignedCoordinates = new HashSet<WorldCoordinates>();

            Loader.modLogger?.LogInfo($"[Conquest-Match] {neutralVillages.Count} villages to be initialized. Allocating {maxCitiesPerPlayer} per player...");

            for (int round = 0; round < maxCitiesPerPlayer; round++)
            {
                for (int p = 0; p < playerCount; p++)
                {
                    PlayerState player = gameState.PlayerStates[p];
                    TileData closestVillage = AssignClosestVillage(neutralVillages, assignedCoordinates, player);

                    if (closestVillage != null)
                    {
                        ConquestInitializeCity(gameState, closestVillage, player);
                    }
                }
            }

            Loader.modLogger?.LogInfo($"[Conquest-Match] All cities initialized successfully!");
        }

        private static TileData AssignClosestVillage(
            List<TileData> neutralVillages, HashSet<WorldCoordinates> assignedCoordinates, PlayerState player)
        {
            WorldCoordinates capitalCoords = player.startTile;
            TileData closestVillage = null;
            int closestDistance = int.MaxValue;

            foreach (var village in neutralVillages)
            {
                if (assignedCoordinates.Contains(village.coordinates)) continue;

                int distance = MapDataExtensions.ManhattanDistance(capitalCoords, village.coordinates);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestVillage = village;
                }
            }

            if (closestVillage != null)
            {
                assignedCoordinates.Add(closestVillage.coordinates);
            }

            return closestVillage;
        }

        // =========================================================================
        // D. Full City Initialization
        // =========================================================================
        private static void ConquestInitializeCity(GameState state, TileData tile, PlayerState player)
        {
            try
            {
                tile.owner = player.Id;
                tile.capitalOf = 0;

                TribeData tribeData;
                if (state.GameLogicData.TryGetData(player.tribe, out tribeData) && tribeData != null)
                {
                    string generatedName = MapDataExtensions.GenerateCityName(state, tile.coordinates, tribeData, player.skinType);
                    if (tile.improvement != null)
                    {
                        tile.improvement.name = generatedName;
                    }
                }

                player.cities++;

                UnitData unitData;
                if (state.GameLogicData.TryGetData(UnitData.Type.Warrior, out unitData))
                {
                    UnitState unitState = ActionUtils.TrainUnitScored(state, player, tile, unitData);
                    unitState.attacked = false;
                    unitState.moved = false;
                }

                Il2CppSystem.Collections.Generic.List<TileData> cityArea = ActionUtils.GetCityAreaSorted(state, tile);
                if (cityArea != null)
                {
                    for (int j = 0; j < cityArea.Count; j++)
                    {
                        TileData territoryTile = cityArea[j];
                        if (territoryTile != null)
                        {
                            territoryTile.owner = player.Id;
                            territoryTile.rulingCityCoordinates = tile.coordinates;
                        }
                    }
                }

                ActionUtils.RuleArea(state, player, tile, false);
                ActionUtils.ExploreFromTile(state, player, tile, 2, false);

                Loader.modLogger?.LogInfo($"[Conquest-Match] City initialized for Player {player.Id} at {tile.coordinates}.");
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Match] City initialization failed: {ex.Message}");
            }
        }

        // =========================================================================
        // E. Tech Cost & City Destruction Handler
        // =========================================================================
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameLogicData), nameof(GameLogicData.GetTechPrice))]
        private static void Conquest_TechCost_Postfix(GameLogicData __instance, TechData techData, PlayerState playerState, GameState state, ref int __result)
        {
            if (state == null || techData == null) return;
            try
            {
                int registeredConquestId = PolyMod.Registry.gameModesAutoidx - 1;
                if ((int)state.Settings.RulesGameMode != registeredConquestId) return;

                int addition = (int)(playerState.cities + state.CurrentTurn);
                addition = Math.Min(addition, 5 + techData.cost * playerState.cities);
                __result = (int)Math.Ceiling((double)(techData.cost + addition));
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Tech] Error: {ex.Message}");
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CaptureCityAction), nameof(CaptureCityAction.ExecuteDefault))]
        private static bool Conquest_CaptureCityAction_Prefix(CaptureCityAction __instance, GameState gameState)
        {
            if (gameState?.Settings == null) return true;
            try
            {
                int registeredConquestId = PolyMod.Registry.gameModesAutoidx - 1;
                if ((int)gameState.Settings.RulesGameMode != registeredConquestId) return true;

                TileData cityTile = gameState.Map.GetTile(__instance.Coordinates);
                PlayerState attacker = null;
                gameState.TryGetPlayer(__instance.PlayerId, out attacker);

                if (cityTile != null && attacker != null)
                    DestroyCityConquest(gameState, cityTile, attacker);

                return false;
            }
            catch
            {
                return true;
            }
        }

        private static void DestroyCityConquest(GameState gameState, TileData cityTile, PlayerState attacker)
        {
            if (cityTile?.improvement?.type != ImprovementData.Type.City) return;

            // 1. Fetch original owner & population
            int transferredPopulation = 0;
            byte originalOwnerId = cityTile.owner;
            PlayerState originalOwner;
            gameState.TryGetPlayer(originalOwnerId, out originalOwner);

            if (originalOwner != null)
            {
                transferredPopulation = cityTile.improvement.population; 

                if (originalOwner.cities > 0)
                {
                    originalOwner.cities--;
                    Loader.modLogger?.LogInfo($"[Conquest] Player {originalOwner.Id} lost a city. Total remaining: {originalOwner.cities}");
                }
            }

            // 2. Transfer population to nearest unsieged city
            if (transferredPopulation > 0 && originalOwner != null)
            {
                TileData fleeCityTile = null;
                int closestDistance = int.MaxValue;

                for (int i = 0; i < gameState.Map.Tiles.Length; i++)
                {
                    TileData tile = gameState.Map.Tiles[i];
                    
                    if (tile.HasImprovement(ImprovementData.Type.City) && tile.owner == originalOwnerId && tile.coordinates != cityTile.coordinates)
                    {
                        bool isSieged = false;
                        
                        if (tile.unit != null && tile.unit.owner != originalOwnerId)
                        {
                            isSieged = true;
                        }

                        if (!isSieged)
                        {
                            int distance = MapDataExtensions.ManhattanDistance(cityTile.coordinates, tile.coordinates);
                            if (distance < closestDistance)
                            {
                                closestDistance = distance;
                                fleeCityTile = tile;
                            }
                        }
                    }
                }

                if (fleeCityTile != null)
                {
                    fleeCityTile.improvement.AddPopulation((short)transferredPopulation);
                    Loader.modLogger?.LogInfo($"[Conquest] Transferred {transferredPopulation} population from razed city to safe city at {fleeCityTile.coordinates}.");

                }
                else
                {
                    Loader.modLogger?.LogInfo($"[Conquest] No safe, un-sieged cities found for Player {originalOwnerId}. Population permanently lost.");
                }
            }

            // 3. Rewards & Scores increment for attacker
            int reward = Math.Min(15, cityTile.improvement.level * 2) + Math.Min(15, (int)gameState.CurrentTurn);
            int score  = 100 + cityTile.improvement.level * 50;
            gameState.ActionStack.Add(new IncreaseScoreAction(attacker.Id, score, cityTile.coordinates, 50));

            if (attacker != null)
            {
                attacker.Currency += reward;
                Loader.modLogger?.LogInfo($"[Conquest] City destroyed by player {attacker.Id} (+{reward} stars & {score} scores)");
            }

            // 4. Unrule city area & Score deduction for defender
            Il2CppSystem.Collections.Generic.List<TileData> cityArea = ActionUtils.GetCityAreaSorted(gameState, cityTile);
            if (cityArea != null)
            {
                for (int j = 0; j < cityArea.Count; j++)
                {
                    TileData territoryTile = cityArea[j];
                    if (territoryTile != null)
                    {
                        int num = ScoreSheet.tileValue;
                        if (territoryTile.improvement != null && territoryTile.coordinates != cityTile.coordinates)
                        {
                            num += gameState.CalculateImprovementScore(territoryTile);
                        }
                        gameState.ActionStack.Add(new DecreaseScoreAction(territoryTile.owner, num));

                        territoryTile.owner = 0;
                        territoryTile.rulingCityCoordinates = WorldCoordinates.NULL_COORDINATES; 
                        // territoryTile.improvement = new ImprovementState { type = ImprovementData.Type.None };
                        territoryTile.improvement = null;
                    }
                }
            }

            // 5. Generate ruins
            bool leaveRuin = UnityEngine.Random.value <= 1f;
            if (leaveRuin)
            {
                cityTile.improvement = new ImprovementState
                {
                    type = ImprovementData.Type.Ruin,
                    borderSize = 0,
                    level = 1,
                    production = 1,
                    founded = 0
                };
            }
            else
            {
                // cityTile.improvement = new ImprovementState { type = ImprovementData.Type.None };
                cityTile.improvement = null;
            }

            cityTile.owner = 0;
            cityTile.capitalOf = 0;

            // 6. Wipe player if necessary
            if (originalOwner != null && attacker != null && !originalOwner.IsAlive(gameState, gameState.Settings.rules.PlayerDeathCondition))
            {
                originalOwner.wipedAtCommandIndex = gameState.CommandStack.Count - 1;
                gameState.ActionStack.Add(new WipePlayerAction(attacker.Id, originalOwner.Id));
            }
            
            Loader.modLogger?.LogInfo($"[Conquest] City at {cityTile.coordinates} has been successfully razed.");
        }

        // =========================================================================
        // F. AI interpretation
        // =========================================================================
        [HarmonyPrefix]
        [HarmonyPatch(typeof(AI), nameof(AI.GetGameProgress))]
        private static bool GetGameProgress_Prefix(ref float __result, GameState gameState, PlayerState winningPlayer)
        {
            if (gameState?.Settings == null) return true;

            try
            {
                int registeredConquestId = PolyMod.Registry.gameModesAutoidx - 1;

                if ((int)gameState.Settings.RulesGameMode == registeredConquestId)
                {
                    if (winningPlayer == null)
                    {
                        __result = 0f;
                        return false;
                    }

                    float totalCities = Math.Max(0.1f, (float)MapDataExtensions.CountCities(gameState));
                    float cityProgress = (float)winningPlayer.cities / totalCities;
                    
                    __result = Math.Min(1f, Math.Max(0f, cityProgress));
                    
                    return false; 
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-AI] Error in GetGameProgress detour: {ex.Message}");
                __result = 0f; 
                return false; 
            }

            return true; 
        }

        // =========================================================================
        // G. Reactions
        // =========================================================================
        [HarmonyPrefix]
        [HarmonyPatch(typeof(CaptureCityReaction), nameof(CaptureCityReaction.Execute))]
        public static bool CaptureCityReaction_Execute_Prefix(CaptureCityReaction __instance, Action onComplete)
        {
            try
            {
                Loader.modLogger?.LogInfo("[Conquest-Popup] Prefix started.");

                int registeredConquestId = PolyMod.Registry.gameModesAutoidx - 1;
                if ((int)GameManager.GameState.Settings.RulesGameMode != registeredConquestId) 
                    return true;

                TileData tile = GameManager.GameState.Map.GetTile(__instance.action.Coordinates);
                PlayerState playerState;
		        GameManager.GameState.TryGetPlayer(__instance.action.PlayerId, out playerState);
		        bool flag = GameManager.IsPlayerViewing(__instance.action.OldOwnerId) && !GameManager.Client.IsSpectating;
		        Tile instance = tile.GetInstance();
                int attackerId = __instance.action.PlayerId;

                CameraController.Instance.CenterOnPosition(tile.coordinates.ToPosition(), 0.8f, null, false);

                ExecutePopupLogic(__instance, onComplete, tile, playerState, instance, attackerId);

                instance?.StopFire();
                if (tile.unit != null)
                {
                    Tile tileInstance = MapRenderer.Current.GetTileInstance(__instance.action.PreviousHomeTown);
                    if (tileInstance != null && !tileInstance.IsHidden)
                    {
                        tileInstance.Render();
                    }
                }
                if (!GameManager.Client.IsReplay)
                    InputEvents.SelectionCleared();
                ResourceManager.IncomeChanged(__instance.action.PlayerId);
                if (!flag)
                {
                    GameManager.DelayCall(2500, onComplete);
                }
                return false;
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Popup] Prefix error: {ex}");
                return true;
            }
        }

        // private static System.Action? _delayActionHolder;
        private static System.Action? _buttonActionHolder;

        private static void ExecutePopupLogic(
            CaptureCityReaction __instance,
            Action onComplete,
            TileData tile,
            PlayerState playerState,
            Tile instance,
            int attackerId)
        {
            try
            {
                Loader.modLogger?.LogInfo("[Conquest-Popup] ExecutePopupLogic started.");

                // Visuals
                if (instance != null)
                {
                    AudioManager.PlaySFXAtTile(SFXTypes.Capture, tile.coordinates, 0, 1f, 1f);
                    instance.Render();
                    instance.SpawnShine(2f);
                    instance.SpawnSparkles(2f);
                    instance.StopFire();
                }

                ReactionUtils.UpdateSurroundingBordersAndTransportPaths((byte)attackerId, tile);
                ResourceManager.AddResourceOfTypeToResourceBar((byte)attackerId, ResourceManager.Type.Score, __instance.action.Score, tile.coordinates, null, "None");

                BasicPopup iconPopup = PopupManager.GetIconPopup();
                if (iconPopup == null)
                {
                    onComplete?.Invoke();
                    return;
                }

                iconPopup.sprite = UIManager.IconData.GetSprite("CapitalCapture");
                bool isCapital = tile.capitalOf != 0;

                if (GameManager.IsPlayerViewing((byte)attackerId) && !GameManager.Client.IsSpectating)
                {
                    // Attacker - No button
                    string title = isCapital ? "Capital conquered!" : "City conquered.";
                    string message = isCapital 
                        ? "The legendary capital has fallen to you. Empire interconnection is forever lost." 
                        : "You have eradicated the city from existence. Infrastructure remains as ruins.";

                    NotificationManager.Notify(message, title, null, playerState);
                }
                else if (GameManager.IsPlayerViewing(__instance.action.OldOwnerId) && !GameManager.Client.IsSpectating)
                {
                    // Defender - With button
                    iconPopup.Header = isCapital ? "Capital conquered!" : "City conquered.";
                    iconPopup.Description = isCapital 
                        ? "Your empire capital has fallen. All interconnection is forever lost." 
                        : "Your city is eradicated from existence. Infrastructure remains as ruins.";

                    _buttonActionHolder = () => onComplete?.Invoke();

                    IntPtr ptr = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_buttonActionHolder);
                    Il2CppSystem.Action il2cppAction = new Il2CppSystem.Action(ptr);

                    var buttonArray = new Il2CppReferenceArray<PopupBase.PopupButtonData>(1);
                    buttonArray[0] = new PopupBase.PopupButtonData(
                        "buttons.ok",
                        PopupBase.PopupButtonData.States.Selected,
                        il2cppAction,                   
                        -1,
                        true,
                        null
                    );

                    iconPopup.buttonData = buttonArray;
                    iconPopup.Show();
                }
                else
                {
                    onComplete?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[Conquest-Popup] ExecutePopupLogic error: {ex}");
                onComplete?.Invoke();
            }
        }
    }
}