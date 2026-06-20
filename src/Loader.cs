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
            Harmony.CreateAndPatchAll(typeof(GameSetup));

            RegisterCustomGameMode("conquest");

            PolyMod.Loader.AddPatchDataType("mapPreset", typeof(MapPreset));
            PolyMod.Loader.AddPatchDataType("mapSize", typeof(MapSize));
            PolyMod.Loader.AddPatchDataType("gameType", typeof(GameType));
            PolyMod.Loader.AddPatchDataType("gameMode", typeof(GameMode));
        
            modLogger?.LogInfo("[Conquest] Mod initialized");
        }

        public static void RegisterCustomGameMode(string id)
        {
            try
            {
                modLogger.LogInfo($"[Conquest-Loader] Initializing custom GameMode registration for key: '{id}'");

                // 1. Double map the string identifier to the next available native index slot
                EnumCache<GameMode>.AddMapping(id, (GameMode)PolyMod.Registry.gameModesAutoidx);
                EnumCache<GameMode>.AddMapping(id, (GameMode)PolyMod.Registry.gameModesAutoidx);
                
                modLogger.LogInfo($"[Conquest-Loader] EnumCache mapping successfully bound to index: {PolyMod.Registry.gameModesAutoidx}");

                // 2. Increment the auto-index counter to keep memory aligned for other mods
                PolyMod.Registry.gameModesAutoidx++;
                modLogger.LogInfo($"[Conquest-Loader] Registration completed. Next index: {PolyMod.Registry.gameModesAutoidx}");
            }
            catch (Exception ex)
            {
                modLogger.LogError($"[Conquest-Loader] FAILURE: Access violation mapping GameMode enum cache: {ex}");
            }
        }
    }
}