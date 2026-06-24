using BepInEx.Logging;
using HarmonyLib;
using PolytopiaBackendBase.Game;
using UnityEngine;
using UnityEngine.EventSystems;
using PolytopiaBackendBase.Common;
using Il2CppInterop.Runtime;
using DG.Tweening;
using Polytopia.Data;


namespace Polyquest
{
    public static class Loader
    {
        public static bool isActive = false;
        public static ManualLogSource? modLogger;

        public static void Load(ManualLogSource logger)
        {
            modLogger = logger;

            Harmony.CreateAndPatchAll(typeof(Main));
            Harmony.CreateAndPatchAll(typeof(Loader));
            Harmony.CreateAndPatchAll(typeof(UI));
            Harmony.CreateAndPatchAll(typeof(UI_2));
            Harmony.CreateAndPatchAll(typeof(Parse));

            RegisterCustomGameMode("conquest");

            PolyMod.Loader.AddPatchDataType("gameType", typeof(GameType));
            PolyMod.Loader.AddPatchDataType("gameMode", typeof(GameMode));
            PolyMod.Loader.AddPatchDataType("gameRules", typeof(GameRules));
        
            modLogger?.LogInfo("[Conquest] Mod initialized");
        }

        public static void RegisterCustomGameMode(string id)
        {
            try
            {
                modLogger?.LogInfo($"[Conquest-Loader] Initializing custom GameMode registration for key: '{id}'");

                // 1. Double map the string identifier to the next available native index slot
                EnumCache<GameMode>.AddMapping(id, (GameMode)PolyMod.Registry.gameModesAutoidx);
                EnumCache<GameMode>.AddMapping(id, (GameMode)PolyMod.Registry.gameModesAutoidx);
                
                modLogger?.LogInfo($"[Conquest-Loader] EnumCache mapping successfully bound to index: {PolyMod.Registry.gameModesAutoidx}");

                // 2. Increment the auto-index counter to keep memory aligned for other mods
                PolyMod.Registry.gameModesAutoidx++;
                modLogger?.LogInfo($"[Conquest-Loader] Registration completed. Next index: {PolyMod.Registry.gameModesAutoidx}");
            }
            catch (Exception ex)
            {
                modLogger?.LogError($"[Conquest-Loader] FAILURE: Access violation mapping GameMode enum cache: {ex}");
            }
        }
        // GAMEMODES //
        public static void SetConquestMode(GameSettings settings, bool enabled)
        {
            if (settings == null) return;
            Parse.conquestModeFlags[settings.RulesGameMode] = enabled;
            modLogger?.LogInfo($"[Conquest] Persistent flag set to {enabled} for mode {settings.RulesGameMode}");
        }

        public static bool IsConquestMode(GameState state)
        {
            if (state?.Settings == null) return false;
            return Parse.conquestModeFlags.TryGetValue(state.Settings.RulesGameMode, out bool val) && val;
        }

        public static bool IsConquestMode(GameSettings settings)
        {
            if (settings == null) return false;
            return Parse.conquestModeFlags.TryGetValue(settings.RulesGameMode, out bool val) && val;
        }
    }
}