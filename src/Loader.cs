using BepInEx.Logging;
using HarmonyLib;
using PolytopiaBackendBase.Game;
using Il2CppInterop.Runtime.Injection;
using Polytopia.Data;
using PolyMod;
using Newtonsoft.Json.Linq;
using PolyMod.Json;

namespace PolyMode
{
    public static class Loader
    {
        public static int opinionAutoidx = Enum.GetValues(typeof(OpinionManager.Type)).Length;
        
        public static ManualLogSource? modLogger;

        public static void Load(ManualLogSource logger)
        {
            modLogger = logger;

            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<CitadelNameOverlay>();
                modLogger?.LogInfo("[Conquest-Loader] CitadelNameOverlay successfully registered in IL2CPP.");
            }
            catch (Exception ex)
            {
                modLogger?.LogError($"[Conquest-Loader] Failed to register custom MonoBehaviours: {ex}");
            }

            // 載入所有 Harmony 補丁
            Harmony.CreateAndPatchAll(typeof(Loader));
            Harmony.CreateAndPatchAll(typeof(Main));
            Harmony.CreateAndPatchAll(typeof(UI_2));
            Harmony.CreateAndPatchAll(typeof(City));
            Harmony.CreateAndPatchAll(typeof(AI2));
            Harmony.CreateAndPatchAll(typeof(MapAnalysisUtils));
            
            Harmony.CreateAndPatchAll(typeof(CitadelNameOverlay));
            Harmony.CreateAndPatchAll(typeof(CitadelOverlayPatches));

            RegisterCustomGameMode("conquest");
            RegisterCustomGameMode("reign");
            //RegisterCustomCityReward("evacuation");
            //RegisterCustomCityReward("valhalla");
            // RegisterCustomCityReward("taxreform");

            PolyMod.Loader.AddPatchDataType("cityReward", typeof(CityReward));
            PolyMod.Loader.AddPatchDataType("opinion", typeof(OpinionManager.Type));
        
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
        public static void RegisterCustomOpinion(string id)
        {
            try
            {
                modLogger?.LogInfo($"[Conquest-Loader] Initializing custom Opinion registration for key: '{id}'");

                // 1. Double map the string identifier to the next available native index slot
                EnumCache<OpinionManager.Type>.AddMapping(id, (OpinionManager.Type)opinionAutoidx);
                EnumCache<OpinionManager.Type>.AddMapping(id, (OpinionManager.Type)opinionAutoidx);

                modLogger?.LogInfo($"[Conquest-Loader] EnumCache mapping successfully bound to index: {opinionAutoidx}");
   
                // 2. Increment the auto-index counter to keep memory aligned for other mods
                opinionAutoidx++;
                modLogger?.LogInfo($"[Conquest-Loader] Registration completed. Next index: {opinionAutoidx}");
            }
            catch (Exception ex)
            {
                modLogger?.LogError($"[Conquest-Loader] FAILURE: Access violation mapping CityReward enum cache: {ex}");
            }
        }
    }
}
