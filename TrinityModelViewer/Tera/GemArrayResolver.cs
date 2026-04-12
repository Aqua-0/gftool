using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Trinity.Core.Flatbuffers.Reflections;
using Trinity.Core.Utils;

namespace TrinityModelViewer.Tera
{
    internal sealed class GemArrayResolver
    {
        internal sealed class GemSettings
        {
            public string ModelLocator { get; init; } = "head";
            public float ModelScale { get; init; } = 1.0f;
            public float[] ModelOffsetPos { get; init; } = new float[] { 0, 0, 0 };
            public float[] ModelEulerAnglesDeg { get; init; } = new float[] { 0, 0, 0 };

            public string EffectLocator { get; init; } = "head";
            public float EffectScale { get; init; } = 1.0f;
            public float[] EffectOffsetPos { get; init; } = new float[] { 0, 0, 0 };
            public float[] EffectEulerAnglesDeg { get; init; } = new float[] { 0, 0, 0 };
        }

        private JsonDocument? cachedDoc;
        private string cachedRoot = string.Empty;
        private Dictionary<string, JsonElement>? cachedByName;

        public bool TryGetSettings(string extractedRoot, int speciesId, int formOrVariant, int gemTypeIndex, out GemSettings settings, out string debug)
        {
            settings = new GemSettings();
            debug = string.Empty;

            if (string.IsNullOrWhiteSpace(extractedRoot) || !Directory.Exists(extractedRoot))
            {
                debug = "no_root";
                return false;
            }

            if (!EnsureLoaded(extractedRoot, out var doc, out debug))
            {
                return false;
            }

            if (cachedByName == null || cachedByName.Count == 0)
            {
                debug = "no_name_index";
                return false;
            }

            // Expected key: "%04d_%d_%02d", then fallback to sex=0, then "_defult"
            // TMV currently doesn't know sex here, so we follow the sex=0 path.
            string key = $"{speciesId:0000}_0_{formOrVariant:00}";
            if (!cachedByName.TryGetValue(key, out var entry))
            {
                if (!cachedByName.TryGetValue("_defult", out entry))
                {
                    debug = $"no_entry(key={key})";
                    return false;
                }
                key = "_defult";
            }

            if (!TryExtractSettings(entry, gemTypeIndex, out settings, out var extractDebug))
            {
                // Some entries are present but incomplete; fall back to the default entry if available.
                if (!string.Equals(key, "_defult", StringComparison.OrdinalIgnoreCase) &&
                    cachedByName.TryGetValue("_defult", out var defEntry) &&
                    TryExtractSettings(defEntry, gemTypeIndex, out settings, out var defDebug))
                {
                    debug = $"ok(key=_defult,{defDebug})";
                    return true;
                }

                debug = $"extract_failed(key={key}):{extractDebug}";
                return false;
            }

            debug = $"ok(key={key},{extractDebug})";
            return true;
        }

        private bool EnsureLoaded(string extractedRoot, out JsonDocument doc, out string debug)
        {
            doc = null!;
            debug = string.Empty;

            if (cachedDoc != null && string.Equals(cachedRoot, extractedRoot, StringComparison.OrdinalIgnoreCase))
            {
                doc = cachedDoc;
                return true;
            }

            string bfbs = Path.Combine(extractedRoot, "world", "data", "gem", "gem", "gem_array.bfbs");
            string bin = Path.Combine(extractedRoot, "world", "data", "gem", "gem", "gem_array.bin");
            if (!File.Exists(bfbs) || !File.Exists(bin))
            {
                debug = "missing_gem_array";
                return false;
            }

            try
            {
                byte[] bfbsBytes = File.ReadAllBytes(bfbs);
                byte[] binBytes = File.ReadAllBytes(bin);
                var reflectionSchema = FlatBufferConverter.DeserializeFrom<ReflectionSchema>(bfbsBytes);
                var ctx = ReflectionSchemaContext.Create(reflectionSchema);
                var json = FlatbufferReflectionJsonDumper.DumpAuto(binBytes, ctx, out _);
                cachedDoc?.Dispose();
                cachedDoc = JsonDocument.Parse(json);
                cachedRoot = extractedRoot;
                doc = cachedDoc;
                cachedByName = BuildNameIndex(doc.RootElement);
                return true;
            }
            catch (Exception ex)
            {
                debug = $"load_failed:{ex.GetType().Name}";
                return false;
            }
        }

        private static Dictionary<string, JsonElement> BuildNameIndex(JsonElement root)
        {
            var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            IndexByName(root, map);
            return map;
        }

        private static void IndexByName(JsonElement el, Dictionary<string, JsonElement> map)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    {
                        if (TryGetPropertyIgnoreCase(el, "name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                        {
                            var name = nameEl.GetString();
                            if (!string.IsNullOrWhiteSpace(name) && LooksLikeGemEntry(el) && !map.ContainsKey(name))
                            {
                                map[name] = el;
                            }
                        }

                        foreach (var prop in el.EnumerateObject())
                        {
                            IndexByName(prop.Value, map);
                        }
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var child in el.EnumerateArray())
                    {
                        IndexByName(child, map);
                    }
                    break;
            }
        }

        private static bool LooksLikeGemEntry(JsonElement obj)
        {
            // We only want the gem_array root entries (indexed by `name`).
            // Avoid capturing unrelated sub-objects that also happen to have a `name`.
            return
                TryFindPropertyRecursive(obj, "IndividualModelData", out _) ||
                TryFindPropertyRecursive(obj, "IndividualModelLocaterData", out _) ||
                TryFindPropertyRecursive(obj, "DefaultEffectData", out _) ||
                TryFindPropertyRecursive(obj, "DefaultEffectLocaterData", out _);
        }

        private static bool TryExtractSettings(JsonElement obj, int gemTypeIndex, out GemSettings settings, out string debug)
        {
            settings = new GemSettings();
            debug = string.Empty;

            // Expected structure:
            // - IndividualModelData.group[gemTypeIndex] => { scale, pos{x,y,z}, rot{x,y,z} }
            // - IndividualModelLocaterData.<TypeName> => locator string
            // - DefaultEffectLocaterData => locator string
            // - DefaultEffectData => { scale, pos{x,y,z}, rot{x,y,z} }

            bool hasIndividualModelData = TryFindPropertyRecursive(obj, "IndividualModelData", out var individualModelData);
            var group = default(JsonElement);
            bool hasGroupProp = hasIndividualModelData && TryGetPropertyIgnoreCase(individualModelData, "group", out group);

            JsonElement groupArray;
            string groupDebug;
            if (hasGroupProp && TryCoerceToArray(group, out groupArray))
            {
                groupDebug = "group=IndividualModelData.group";
            }
            else if (hasGroupProp && TryReadArrayLike(group, gemTypeIndex, out groupArray, out var arrayLikeDebug))
            {
                groupDebug = $"group=IndividualModelData.group({arrayLikeDebug})";
            }
            else if (hasIndividualModelData && TryFindTransformGroupArrayRecursive(individualModelData, out groupArray))
            {
                groupDebug = "group=fallback(IndividualModelData.transform_array)";
            }
            else if (TryFindTransformGroupArrayRecursive(obj, out groupArray))
            {
                groupDebug = "group=fallback(entry.transform_array)";
            }
            else
            {
                var indPreview = hasIndividualModelData ? GetObjectKeysPreview(individualModelData) : "(none)";
                debug = $"missing_IndividualModelData.group(hasInd={hasIndividualModelData},hasGroup={hasGroupProp},indKeys={indPreview})";
                return false;
            }

            if (!TryReadArrayLike(groupArray, gemTypeIndex, out var modelGroup, out var groupLen, out var groupIndexDebug))
            {
                debug = $"bad_gemTypeIndex({gemTypeIndex}/{groupLen})";
                return false;
            }

            if (modelGroup.ValueKind == JsonValueKind.Object &&
                (TryGetFloatFlex(modelGroup, "scale") == null || TryGetVec3Flex(modelGroup, "pos") == null || TryGetVec3Flex(modelGroup, "rot") == null) &&
                TryFindTransformObjectRecursive(modelGroup, out var modelGroupTransform))
            {
                modelGroup = modelGroupTransform;
                groupIndexDebug += "+wrapped";
            }

            float modelScale = TryGetFloatFlex(modelGroup, "scale") ?? 1.0f;
            float[] modelOffset = TryGetVec3Flex(modelGroup, "pos") ?? new float[] { 0, 0, 0 };
            float[] modelEuler = TryGetVec3Flex(modelGroup, "rot") ?? new float[] { 0, 0, 0 };

            string modelLocator = "head";
            string locatorDebug = "locator=default";
            if (TryFindPropertyRecursive(obj, "IndividualModelLocaterData", out var locatorData) ||
                TryFindPropertyRecursive(obj, "IndividualModelLocatorData", out locatorData))
            {
                locatorDebug = "locator=IndividualModelLocaterData";
                modelLocator = ResolveLocator(locatorData, gemTypeIndex) ?? "head";
            }
            else if (TryFindLocatorMapRecursive(obj, out locatorData))
            {
                locatorDebug = "locator=fallback(locator_map)";
                modelLocator = ResolveLocator(locatorData, gemTypeIndex) ?? "head";
            }

            string effectLocator = TryFindStringRecursive(obj, "DefaultEffectLocaterData") ??
                                  TryFindStringRecursive(obj, "DefaultEffectLocatorData") ??
                                  modelLocator;
            float effectScale = 1.0f;
            float[] effectOffset = new float[] { 0, 0, 0 };
            float[] effectEuler = new float[] { 0, 0, 0 };
            JsonElement effectData;
            if ((TryFindPropertyRecursive(obj, "DefaultEffectData", out effectData) ||
                 TryFindPropertyRecursive(obj, "DefaultEffect", out effectData)) &&
                effectData.ValueKind == JsonValueKind.Object)
            {
                if ((TryGetFloatFlex(effectData, "scale") == null || TryGetVec3Flex(effectData, "pos") == null || TryGetVec3Flex(effectData, "rot") == null) &&
                    TryFindTransformObjectRecursive(effectData, out var effectTransform))
                {
                    effectData = effectTransform;
                }

                effectScale = TryGetFloatFlex(effectData, "scale") ?? modelScale;
                effectOffset = TryGetVec3Flex(effectData, "pos") ?? effectOffset;
                effectEuler = TryGetVec3Flex(effectData, "rot") ?? effectEuler;
            }
            else if (TryFindTransformObjectRecursive(obj, out effectData))
            {
                // Best-effort fallback: first transform-like object in the entry.
                effectScale = TryGetFloatFlex(effectData, "scale") ?? modelScale;
                effectOffset = TryGetVec3Flex(effectData, "pos") ?? effectOffset;
                effectEuler = TryGetVec3Flex(effectData, "rot") ?? effectEuler;
            }
            else
            {
                effectScale = modelScale;
            }

            settings = new GemSettings
            {
                ModelLocator = modelLocator,
                ModelScale = modelScale,
                ModelOffsetPos = modelOffset,
                ModelEulerAnglesDeg = modelEuler,
                EffectLocator = effectLocator,
                EffectScale = effectScale,
                EffectOffsetPos = effectOffset,
                EffectEulerAnglesDeg = effectEuler
            };
            debug = $"{groupDebug},{groupIndexDebug},{locatorDebug}";
            return true;
        }

        private static bool HasPropertyIgnoreCase(JsonElement obj, string key)
        {
            return TryGetPropertyIgnoreCase(obj, key, out _);
        }

        private static bool TryFindPropertyRecursive(JsonElement el, string key, out JsonElement value)
        {
            value = default;
            if (TryGetPropertyIgnoreCase(el, key, out value))
            {
                return true;
            }

            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var prop in el.EnumerateObject())
                    {
                        if (TryFindPropertyRecursive(prop.Value, key, out value))
                        {
                            return true;
                        }
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var child in el.EnumerateArray())
                    {
                        if (TryFindPropertyRecursive(child, key, out value))
                        {
                            return true;
                        }
                    }
                    break;
            }

            return false;
        }

        private static string? TryFindStringRecursive(JsonElement el, string key)
        {
            return TryFindPropertyRecursive(el, key, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
        }

        private static string? ResolveLocator(JsonElement locatorData, int gemTypeIndex)
        {
            string typeName = GemTypeNameFromIndex(gemTypeIndex);
            if (locatorData.ValueKind == JsonValueKind.Object)
            {
                return TryGetString(locatorData, typeName) ?? TryGetStringIgnoreCase(locatorData, typeName);
            }

            if (locatorData.ValueKind == JsonValueKind.Array)
            {
                int len = locatorData.GetArrayLength();
                if (gemTypeIndex >= 0 && gemTypeIndex < len)
                {
                    var el = locatorData[gemTypeIndex];
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        return el.GetString();
                    }
                }
            }

            return null;
        }

        private static bool TryFindTransformGroupArrayRecursive(JsonElement el, out JsonElement array)
        {
            array = default;
            if (el.ValueKind == JsonValueKind.Array)
            {
                int len = el.GetArrayLength();
                if (len >= 18)
                {
                    bool ok = true;
                    for (int i = 0; i < Math.Min(len, 4); i++)
                    {
                        var item = el[i];
                        if (item.ValueKind != JsonValueKind.Object)
                        {
                            ok = false;
                            break;
                        }

                        if (TryGetFloatFlex(item, "scale") == null || TryGetVec3Flex(item, "pos") == null || TryGetVec3Flex(item, "rot") == null)
                        {
                            if (!TryFindTransformObjectRecursive(item, out _))
                            {
                                ok = false;
                                break;
                            }
                        }
                    }
                    if (ok)
                    {
                        array = el;
                        return true;
                    }
                }
            }

            if (el.ValueKind == JsonValueKind.Object)
            {
                // Some reflection dumps encode arrays as objects with numeric keys ("0","1",...).
                // Detect that pattern for transform groups.
                var numericChildren = new List<(int Idx, JsonElement Val)>();
                foreach (var prop in el.EnumerateObject())
                {
                    if (int.TryParse(prop.Name, out int idx))
                    {
                        numericChildren.Add((idx, prop.Value));
                    }
                }

                if (numericChildren.Count >= 18)
                {
                    numericChildren.Sort((a, b) => a.Idx.CompareTo(b.Idx));
                    bool ok = true;
                    for (int i = 0; i < Math.Min(numericChildren.Count, 4); i++)
                    {
                        var item = numericChildren[i].Val;
                        if (item.ValueKind != JsonValueKind.Object)
                        {
                            ok = false;
                            break;
                        }

                        if (TryGetFloatFlex(item, "scale") == null || TryGetVec3Flex(item, "pos") == null || TryGetVec3Flex(item, "rot") == null)
                        {
                            if (!TryFindTransformObjectRecursive(item, out _))
                            {
                                ok = false;
                                break;
                            }
                        }
                    }

                    if (ok)
                    {
                        array = el;
                        return true;
                    }
                }

                foreach (var prop in el.EnumerateObject())
                {
                    if (TryFindTransformGroupArrayRecursive(prop.Value, out array))
                    {
                        return true;
                    }
                }
            }
            else if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in el.EnumerateArray())
                {
                    if (TryFindTransformGroupArrayRecursive(child, out array))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryFindTransformObjectRecursive(JsonElement el, out JsonElement obj)
        {
            obj = default;
            if (el.ValueKind == JsonValueKind.Object)
            {
                if (TryGetFloatFlex(el, "scale") != null && TryGetVec3Flex(el, "pos") != null && TryGetVec3Flex(el, "rot") != null)
                {
                    obj = el;
                    return true;
                }

                foreach (var prop in el.EnumerateObject())
                {
                    if (TryFindTransformObjectRecursive(prop.Value, out obj))
                    {
                        return true;
                    }
                }
            }
            else if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in el.EnumerateArray())
                {
                    if (TryFindTransformObjectRecursive(child, out obj))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryFindLocatorMapRecursive(JsonElement el, out JsonElement locatorData)
        {
            locatorData = default;
            if (el.ValueKind == JsonValueKind.Object)
            {
                // Locator maps usually contain the type-name keys like "Normal", "Kakutou", ...
                if (TryGetString(el, "Normal") != null || TryGetStringIgnoreCase(el, "Normal") != null)
                {
                    locatorData = el;
                    return true;
                }

                foreach (var prop in el.EnumerateObject())
                {
                    if (TryFindLocatorMapRecursive(prop.Value, out locatorData))
                    {
                        return true;
                    }
                }
            }
            else if (el.ValueKind == JsonValueKind.Array)
            {
                // Fallback: an array of locator strings by gem type index.
                int len = el.GetArrayLength();
                if (len >= 18 && el[0].ValueKind == JsonValueKind.String)
                {
                    locatorData = el;
                    return true;
                }

                foreach (var child in el.EnumerateArray())
                {
                    if (TryFindLocatorMapRecursive(child, out locatorData))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryGetPropertyIgnoreCase(JsonElement obj, string key, out JsonElement value)
        {
            value = default;
            if (obj.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (obj.TryGetProperty(key, out value))
            {
                return true;
            }

            foreach (var prop in obj.EnumerateObject())
            {
                if (string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }

            return false;
        }

        private static bool TryCoerceToArray(JsonElement el, out JsonElement array)
        {
            array = default;
            if (el.ValueKind == JsonValueKind.Array)
            {
                array = el;
                return true;
            }

            // Some dumps might represent arrays as objects with numeric keys.
            if (el.ValueKind == JsonValueKind.Object)
            {
                // We treat it as "array-like" in TryReadArrayLike.
                array = el;
                return true;
            }

            return false;
        }

        private static float? TryGetFloatFlex(JsonElement obj, string key)
        {
            var f = TryGetFloat(obj, key);
            if (f != null)
            {
                return f;
            }

            if (key.Equals("scale", StringComparison.OrdinalIgnoreCase))
            {
                return TryGetFloat(obj, "scl");
            }

            return null;
        }

        private static float[]? TryGetVec3Flex(JsonElement obj, string key)
        {
            if (key.Equals("pos", StringComparison.OrdinalIgnoreCase))
            {
                return TryGetVec3(obj, "pos") ?? TryGetVec3(obj, "position") ?? TryGetVec3(obj, "translation") ?? TryGetVec3(obj, "translate");
            }

            if (key.Equals("rot", StringComparison.OrdinalIgnoreCase))
            {
                return TryGetVec3(obj, "rot") ?? TryGetVec3(obj, "rotation") ?? TryGetVec3(obj, "euler") ?? TryGetVec3(obj, "angles");
            }

            return TryGetVec3(obj, key);
        }

        private static string GetObjectKeysPreview(JsonElement obj, int maxKeys = 10)
        {
            try
            {
                if (obj.ValueKind != JsonValueKind.Object)
                {
                    return obj.ValueKind.ToString();
                }

                var keys = new List<string>(maxKeys);
                foreach (var p in obj.EnumerateObject())
                {
                    keys.Add(p.Name);
                    if (keys.Count >= maxKeys)
                    {
                        break;
                    }
                }
                return keys.Count == 0 ? "(empty)" : string.Join("|", keys);
            }
            catch
            {
                return "(error)";
            }
        }

        private static string? TryGetStringIgnoreCase(JsonElement obj, string key)
        {
            if (!TryGetPropertyIgnoreCase(obj, key, out var el))
            {
                return null;
            }
            return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
        }

        private static bool TryReadArrayLike(JsonElement el, int index, out JsonElement value, out int length, out string debug)
        {
            value = default;
            length = 0;
            debug = "arraylike=none";

            if (el.ValueKind == JsonValueKind.Array)
            {
                length = el.GetArrayLength();
                debug = "arraylike=array";
                if (index < 0 || index >= length)
                {
                    return false;
                }
                value = el[index];
                return true;
            }

            if (el.ValueKind == JsonValueKind.Object)
            {
                // Try direct numeric key.
                var key = index.ToString();
                if (TryGetPropertyIgnoreCase(el, key, out value))
                {
                    length = 0;
                    foreach (var _ in el.EnumerateObject()) length++;
                    debug = "arraylike=object_numeric";
                    return true;
                }

                // As a fallback, try to enumerate numeric keys and pick by sorted order.
                var pairs = new List<(int Idx, JsonElement Val)>();
                foreach (var prop in el.EnumerateObject())
                {
                    if (int.TryParse(prop.Name, out int idx))
                    {
                        pairs.Add((idx, prop.Value));
                    }
                }
                if (pairs.Count != 0)
                {
                    pairs.Sort((a, b) => a.Idx.CompareTo(b.Idx));
                    length = pairs.Count;
                    debug = "arraylike=object_sorted";
                    if (index < 0 || index >= length)
                    {
                        return false;
                    }
                    value = pairs[index].Val;
                    return true;
                }
            }

            return false;
        }

        private static bool TryReadArrayLike(JsonElement el, int index, out JsonElement value, out string debug)
        {
            return TryReadArrayLike(el, index, out value, out _, out debug);
        }

        private static string GemTypeNameFromIndex(int gemTypeIndex)
        {
            // Matches the observed gem type name table indexed by gem type id.
            return gemTypeIndex switch
            {
                0 => "Normal",
                1 => "Kakutou",
                2 => "Hikou",
                3 => "Doku",
                4 => "Jimen",
                5 => "Iwa",
                6 => "Mushi",
                7 => "Ghost",
                8 => "Hagane",
                9 => "Honoo",
                10 => "Mizu",
                11 => "Kusa",
                12 => "Denki",
                13 => "Esper",
                14 => "Koori",
                15 => "Dragon",
                16 => "Aku",
                17 => "Fairy",
                _ => "Normal"
            };
        }

        private static string? TryGetString(JsonElement obj, string key)
        {
            if (!TryGetPropertyIgnoreCase(obj, key, out var el))
            {
                return null;
            }
            return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
        }

        private static float? TryGetFloat(JsonElement obj, string key)
        {
            if (!TryGetPropertyIgnoreCase(obj, key, out var el))
            {
                return null;
            }
            if (el.ValueKind == JsonValueKind.Number && el.TryGetSingle(out float f))
            {
                return f;
            }
            if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out double d))
            {
                return (float)d;
            }
            if (el.ValueKind == JsonValueKind.String && float.TryParse(el.GetString(), out float parsed))
            {
                return parsed;
            }
            return null;
        }

        private static float[]? TryGetVec3(JsonElement obj, string key)
        {
            if (!TryGetPropertyIgnoreCase(obj, key, out var el))
            {
                return null;
            }

            // Some dumps serialize Vector3 as object {x,y,z}; others as array [x,y,z].
            if (el.ValueKind == JsonValueKind.Object)
            {
                float x = TryGetFloat(el, "x") ?? 0;
                float y = TryGetFloat(el, "y") ?? 0;
                float z = TryGetFloat(el, "z") ?? 0;
                return new[] { x, y, z };
            }

            if (el.ValueKind == JsonValueKind.Array)
            {
                var vals = new List<float>(3);
                foreach (var v in el.EnumerateArray())
                {
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetSingle(out float f))
                    {
                        vals.Add(f);
                    }
                    else if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d))
                    {
                        vals.Add((float)d);
                    }
                }
                if (vals.Count >= 3)
                {
                    return new[] { vals[0], vals[1], vals[2] };
                }
            }

            return null;
        }
    }
}
