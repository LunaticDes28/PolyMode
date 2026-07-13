using BepInEx.Logging;
using HarmonyLib;
using PolytopiaBackendBase.Game;
using Il2CppInterop.Runtime.Injection;

namespace PolyMode
{
    public static class Loader
    {
        public static bool isActive = false;
        public static ManualLogSource? modLogger;

        public static void Load(ManualLogSource logger)
        {
            modLogger = logger;

            try
            {
                // ⭕ 【關鍵修正 1】在所有 Patch 載入前，將繼承自 MonoBehaviour 的自訂 UI 元件註冊進 IL2CPP
                // 註：請將 "CitadelNameOverlay" 替換成你真正繼承了 MonoBehaviour 的那個類別名稱
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
            
            Harmony.CreateAndPatchAll(typeof(CitadelNameOverlay));
            Harmony.CreateAndPatchAll(typeof(CitadelOverlayPatches));

            RegisterCustomGameMode("conquest");
            RegisterCustomGameMode("reign");

            PolyMod.Loader.AddPatchDataType("conquest", typeof(CityReward));
        
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
    }
}
