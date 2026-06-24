using BepInEx.Logging;
using HarmonyLib;
using Polytopia.Data;
using UnityEngine;

using Newtonsoft.Json.Linq;
using Il2CppSystem.Linq;

using pbb = PolytopiaBackendBase.Common;
using Steamworks.Data;
using MS.Internal.Xml.XPath;
using PolyMod;

using System;
using System.Reflection;
using System.Collections.Generic;
using PolytopiaBackendBase.Game;

namespace Polyquest;
public static class Parse
{
    public static Dictionary<GameMode, bool> conquestModeFlags = new Dictionary<GameMode, bool>();

    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(GameLogicData), nameof(GameLogicData.AddGameLogicPlaceholders))]
    private static void GameLogicData_Parse(GameLogicData __instance, JObject rootObject)
    {
        // Loader.modLogger?.LogInfo("=== Starting parsing ===");
        
        // ParsePerEach(rootObject, "gameRules", "conquest", conquestModeFlags);

        // Loader.modLogger?.LogInfo($"Parsed conquest mode entries: {conquestModeFlags.Count}");
    }

    public static void ParsePerEach<targetType, T>(
        JObject rootObject,
        string categoryName,
        string fieldName,
        Dictionary<targetType, T> dict,
        string[]? nestedContainers = null)
        where targetType : struct, System.IConvertible
    {
        Loader.modLogger?.LogInfo($"ParsePerEach: Looking for {categoryName}.{fieldName}");

        // Safest way to get tokens without foreach
        var tokenEnumerable = rootObject.SelectTokens($"$.{categoryName}.*");
        var tokens = new List<JToken>();
        for (int i = 0; ; i++)
        {
            try
            {
                JToken? t = tokenEnumerable.ElementAt(i);
                if (t == null) break;
                tokens.Add(t);
            }
            catch { break; }
        }

        Loader.modLogger?.LogInfo($"Found {tokens.Count} entries in {categoryName}");

        for (int i = 0; i < tokens.Count; i++)
        {
            JObject? token = tokens[i].TryCast<JObject>();
            if (token == null) continue;

            string name = token.Path.Split('.').Last();
            Loader.modLogger?.LogInfo($"  Checking type: {name}");

            if (!EnumCache<targetType>.TryGetType(name, out var type))
            {
                Loader.modLogger?.LogInfo($"    → EnumCache failed for {name}");
                continue;
            }

            Loader.modLogger?.LogInfo($"    → Enum resolved: {type}");

            T? value = default;

            // Top level
            if (TryExtractAndRemove(token, fieldName, out value))
            {
                if (value != null)
                {
                    dict[type] = value;
                    Loader.modLogger?.LogInfo($"    SUCCESS (top-level) for {type}");
                }
                continue;
            }

            // Nested
            if (nestedContainers != null)
            {
                for (int c = 0; c < nestedContainers.Length; c++)
                {
                    string container = nestedContainers[c];
                    Loader.modLogger?.LogInfo($"    Checking nested container: {container}");

                    if (TryFindInNested(token, container, fieldName, out value))
                    {
                        if (value != null)
                        {
                            dict[type] = value;
                            Loader.modLogger?.LogInfo($"    ✅ SUCCESS! Parsed {fieldName} for {type} = {value}");
                        }
                        break;
                    }
                }
            }
        }

        Loader.modLogger?.LogInfo($"ParsePerEach finished. Total entries in dict: {dict.Count}");
    }

    private static bool TryExtractAndRemove<TVal>(JObject token, string fieldName, out TVal? value)
    {
        value = default;
        JToken? fieldToken = token[fieldName];
        if (fieldToken == null) return false;

        value = fieldToken.ToObject<TVal>();
        token.Remove(fieldName);
        return true;
    }

    private static bool TryFindInNested<TVal>(JObject token, string containerName, string fieldName, out TVal? value)
    {
        value = default;
        JToken? container = token[containerName];
        if (container == null || container.Type != JTokenType.Array) 
            return false;

        JArray? array = container.TryCast<JArray>();
        if (array == null) return false;

        Loader.modLogger?.LogInfo($"      JArray Count = {array.Count}");

        for (int j = 0; j < array.Count; j++)
        {
            JObject? obj = array[j]?.TryCast<JObject>();
            if (obj == null) continue;

            JToken? customField = obj[fieldName];
            if (customField == null) continue;

            Loader.modLogger?.LogInfo($"        Found '{fieldName}' of type {customField.Type}");

            try
            {
                // === Handle Arrays (int[], float[], string[], etc.) ===
                if (typeof(TVal).IsArray)
                {
                    JArray? jarr = customField.TryCast<JArray>();
                    if (jarr != null)
                    {
                        // Simple manual conversion for common array types
                        if (typeof(TVal) == typeof(int[]))
                        {
                            int[] result = new int[jarr.Count];
                            for (int k = 0; k < jarr.Count; k++)
                                result[k] = jarr[k].ToObject<int>();
                            value = (TVal)(object)result;
                        }
                        else if (typeof(TVal) == typeof(float[]))
                        {
                            float[] result = new float[jarr.Count];
                            for (int k = 0; k < jarr.Count; k++)
                                result[k] = jarr[k].ToObject<float>();
                            value = (TVal)(object)result;
                        }
                        else if (typeof(TVal) == typeof(string[]))
                        {
                            string[] result = new string[jarr.Count];
                            for (int k = 0; k < jarr.Count; k++)
                                result[k] = jarr[k].ToObject<string>();
                            value = (TVal)(object)result;
                        }
                        else
                        {
                            // Fallback for other array types
                            value = customField.ToObject<TVal>();
                        }

                        obj.Remove(fieldName);
                        Loader.modLogger?.LogInfo($"        ✅ Parsed array for {typeof(TVal)}");
                        return true;
                    }
                }
                // === Handle normal (non-array) types ===
                else
                {
                    value = customField.ToObject<TVal>();
                    obj.Remove(fieldName);
                    Loader.modLogger?.LogInfo($"        ✅ Parsed single value for {typeof(TVal)}");
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                Loader.modLogger?.LogInfo($"        Error parsing {fieldName} as {typeof(TVal)}: {ex.Message}");
            }
        }

        return false;
    }
    public static bool TryGetValue<T1, T2, T3>(List<T1> list, T2 type, string fieldName, out T3 result)
    {
        int index = FindData(list, type);
        if(index == -1)
        {
            result = default;
            return false;
        }
        object obj = list[index].GetType().GetField(fieldName).GetValue(list[index]);
        if(obj is T3 value && !EqualityComparer<T3>.Default.Equals(value, default(T3)))
        {
            result = value;
            return true;
        }

        result = default;
        return false;
    }

    public static int FindData<T1, T2>(List<T1> list, T2 type)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var item = list[i];

            var field = item?.GetType().GetField("type");
            if (field == null)
                continue;

            var value = field.GetValue(item);

            if (value is T2 typedValue && EqualityComparer<T2>.Default.Equals(typedValue, type))
            {
                return i;
            }
        }

        return -1;
    }

    // GAMEMODES //
    private static bool _isConquestMode = false;

    public static void SetConquestMode(bool value)
    {
        _isConquestMode = value;
        Loader.modLogger?.LogInfo($"[Conquest] Global flag set to: {value}");
    }

    public static bool IsConquestMode()
    {
        return _isConquestMode;
    }
}