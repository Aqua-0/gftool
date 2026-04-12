using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trinity.Core.Flatbuffers.TR.Model;
using Trinity.Core.Flatbuffers.Utils;

namespace TrinityModelViewer
{
    internal static class TrmtrFileJson
    {
        public static TrmtrFile Deserialize(string json)
        {
            var trmtr = JsonSerializer.Deserialize<TrmtrFile>(json, CreateOptions(prettySamplers: false));
            if (trmtr == null)
            {
                throw new InvalidOperationException("JSON did not parse into TRMTR.");
            }
            return trmtr;
        }

        public static string Serialize(TrmtrFile trmtr, bool prettySamplers)
        {
            if (trmtr == null) throw new ArgumentNullException(nameof(trmtr));
            return JsonSerializer.Serialize(trmtr, CreateOptions(prettySamplers));
        }

        private static JsonSerializerOptions CreateOptions(bool prettySamplers)
        {
            var opts = new JsonSerializerOptions
            {
                WriteIndented = true,
            };
            opts.Converters.Add(new TrmtrFileSamplerStateJsonConverter(prettySamplers));
            return opts;
        }

        private sealed class TrmtrFileSamplerStateJsonConverter : JsonConverter<TrmtrFileSamplerState>
        {
            private readonly bool prettyWrite;

            public TrmtrFileSamplerStateJsonConverter(bool prettyWrite)
            {
                this.prettyWrite = prettyWrite;
            }

            public override TrmtrFileSamplerState? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                var root = doc.RootElement;

                var result = new TrmtrFileSamplerState();

                if (TryGetPropertyIgnoreCase(root, "states", out var states) && states.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<uint>(9);
                    foreach (var el in states.EnumerateArray())
                    {
                        if (TryReadUInt32(el, out var v))
                        {
                            list.Add(v);
                        }
                    }

                    // Pad missing entries.
                    while (list.Count < 9)
                    {
                        list.Add(0);
                    }

                    result.State0 = list[0];
                    result.State1 = list[1];
                    result.State2 = list[2];
                    result.State3 = list[3];
                    result.State4 = list[4];
                    result.State5 = list[5];
                    result.State6 = list[6];
                    result.State7 = list[7];
                    result.State8 = list[8];
                }
                else
                {
                    result.State0 = ReadUInt32Property(root, "State0", fallback: 0);
                    result.State1 = ReadUInt32Property(root, "State1", fallback: 0);
                    result.State2 = ReadUInt32Property(root, "State2", fallback: 0);
                    result.State3 = ReadUInt32Property(root, "State3", fallback: 0);
                    result.State4 = ReadUInt32Property(root, "State4", fallback: 0);
                    result.State5 = ReadUInt32Property(root, "State5", fallback: 0);
                    result.State6 = ReadUInt32Property(root, "State6", fallback: 0);
                    result.State7 = ReadUInt32Property(root, "State7", fallback: 0);
                    result.State8 = ReadUInt32Property(root, "State8", fallback: 0);
                }

                if (TryGetPropertyIgnoreCase(root, "wrap", out var wrap) && wrap.ValueKind == JsonValueKind.Object)
                {
                    result.RepeatU = ReadWrapModeProperty(wrap, "u", fallback: UVWrapMode.WRAP);
                    result.RepeatV = ReadWrapModeProperty(wrap, "v", fallback: UVWrapMode.WRAP);
                    result.RepeatW = ReadWrapModeProperty(wrap, "w", fallback: UVWrapMode.WRAP);
                }
                else
                {
                    result.RepeatU = ReadWrapModeProperty(root, "RepeatU", fallback: UVWrapMode.WRAP);
                    result.RepeatV = ReadWrapModeProperty(root, "RepeatV", fallback: UVWrapMode.WRAP);
                    result.RepeatW = ReadWrapModeProperty(root, "RepeatW", fallback: UVWrapMode.WRAP);
                }

                if (TryGetPropertyIgnoreCase(root, "borderColor", out var bc) && bc.ValueKind == JsonValueKind.Object)
                {
                    result.BorderColor = new RGBA
                    {
                        R = ReadFloatProperty(bc, "r", fallback: ReadFloatProperty(bc, "R", 0f)),
                        G = ReadFloatProperty(bc, "g", fallback: ReadFloatProperty(bc, "G", 0f)),
                        B = ReadFloatProperty(bc, "b", fallback: ReadFloatProperty(bc, "B", 0f)),
                        A = ReadFloatProperty(bc, "a", fallback: ReadFloatProperty(bc, "A", 0f)),
                    };
                }
                else if (TryGetPropertyIgnoreCase(root, "BorderColor", out var bc2) && bc2.ValueKind == JsonValueKind.Object)
                {
                    result.BorderColor = new RGBA
                    {
                        R = ReadFloatProperty(bc2, "R", fallback: 0f),
                        G = ReadFloatProperty(bc2, "G", fallback: 0f),
                        B = ReadFloatProperty(bc2, "B", fallback: 0f),
                        A = ReadFloatProperty(bc2, "A", fallback: 0f),
                    };
                }

                return result;
            }

            public override void Write(Utf8JsonWriter writer, TrmtrFileSamplerState value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();

                if (prettyWrite)
                {
                    writer.WritePropertyName("states");
                    writer.WriteStartArray();
                    WriteHex(writer, value.State0);
                    WriteHex(writer, value.State1);
                    WriteHex(writer, value.State2);
                    WriteHex(writer, value.State3);
                    WriteHex(writer, value.State4);
                    WriteHex(writer, value.State5);
                    WriteHex(writer, value.State6);
                    WriteHex(writer, value.State7);
                    WriteHex(writer, value.State8);
                    writer.WriteEndArray();

                    writer.WritePropertyName("wrap");
                    writer.WriteStartObject();
                    writer.WriteString("u", WrapModeToString(value.RepeatU));
                    writer.WriteString("v", WrapModeToString(value.RepeatV));
                    writer.WriteString("w", WrapModeToString(value.RepeatW));
                    writer.WriteEndObject();

                    writer.WritePropertyName("borderColor");
                    writer.WriteStartObject();
                    writer.WriteNumber("r", value.BorderColor?.R ?? 0f);
                    writer.WriteNumber("g", value.BorderColor?.G ?? 0f);
                    writer.WriteNumber("b", value.BorderColor?.B ?? 0f);
                    writer.WriteNumber("a", value.BorderColor?.A ?? 0f);
                    writer.WriteEndObject();
                }
                else
                {
                    writer.WriteNumber("State0", value.State0);
                    writer.WriteNumber("State1", value.State1);
                    writer.WriteNumber("State2", value.State2);
                    writer.WriteNumber("State3", value.State3);
                    writer.WriteNumber("State4", value.State4);
                    writer.WriteNumber("State5", value.State5);
                    writer.WriteNumber("State6", value.State6);
                    writer.WriteNumber("State7", value.State7);
                    writer.WriteNumber("State8", value.State8);
                    writer.WriteNumber("RepeatU", (uint)value.RepeatU);
                    writer.WriteNumber("RepeatV", (uint)value.RepeatV);
                    writer.WriteNumber("RepeatW", (uint)value.RepeatW);
                    writer.WritePropertyName("BorderColor");
                    JsonSerializer.Serialize(writer, value.BorderColor ?? new RGBA(), options);
                }

                writer.WriteEndObject();
            }

            private static void WriteHex(Utf8JsonWriter writer, uint value)
            {
                writer.WriteStringValue($"0x{value:X8}");
            }

            private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
            {
                if (obj.ValueKind != JsonValueKind.Object)
                {
                    value = default;
                    return false;
                }

                foreach (var p in obj.EnumerateObject())
                {
                    if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = p.Value;
                        return true;
                    }
                }

                value = default;
                return false;
            }

            private static uint ReadUInt32Property(JsonElement obj, string name, uint fallback)
            {
                if (!TryGetPropertyIgnoreCase(obj, name, out var el))
                {
                    return fallback;
                }

                return TryReadUInt32(el, out var v) ? v : fallback;
            }

            private static float ReadFloatProperty(JsonElement obj, string name, float fallback)
            {
                if (!TryGetPropertyIgnoreCase(obj, name, out var el))
                {
                    return fallback;
                }

                if (el.ValueKind == JsonValueKind.Number && el.TryGetSingle(out var f))
                {
                    return f;
                }

                if (el.ValueKind == JsonValueKind.String &&
                    float.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var fs))
                {
                    return fs;
                }

                return fallback;
            }

            private static bool TryReadUInt32(JsonElement el, out uint value)
            {
                if (el.ValueKind == JsonValueKind.Number && el.TryGetUInt32(out value))
                {
                    return true;
                }

                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString()?.Trim();
                    if (string.IsNullOrWhiteSpace(s))
                    {
                        value = 0;
                        return false;
                    }

                    if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        return uint.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
                    }

                    if (uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                    {
                        return true;
                    }
                }

                value = 0;
                return false;
            }

            private static UVWrapMode ReadWrapModeProperty(JsonElement obj, string name, UVWrapMode fallback)
            {
                if (!TryGetPropertyIgnoreCase(obj, name, out var el))
                {
                    return fallback;
                }

                if (el.ValueKind == JsonValueKind.Number && el.TryGetUInt32(out var v))
                {
                    return (UVWrapMode)v;
                }

                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = (el.GetString() ?? string.Empty).Trim();
                    if (TryParseWrapModeString(s, out var parsed))
                    {
                        return parsed;
                    }
                }

                return fallback;
            }

            private static bool TryParseWrapModeString(string s, out UVWrapMode mode)
            {
                if (string.IsNullOrWhiteSpace(s))
                {
                    mode = default;
                    return false;
                }

                // Friendly names.
                if (string.Equals(s, "Repeat", StringComparison.OrdinalIgnoreCase))
                {
                    mode = UVWrapMode.WRAP;
                    return true;
                }
                if (string.Equals(s, "Clamp", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "ClampToEdge", StringComparison.OrdinalIgnoreCase))
                {
                    mode = UVWrapMode.CLAMP;
                    return true;
                }
                if (string.Equals(s, "MirroredRepeat", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "Mirror", StringComparison.OrdinalIgnoreCase))
                {
                    mode = UVWrapMode.MIRROR;
                    return true;
                }
                if (string.Equals(s, "MirrorOnce", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "MirrorClampToEdge", StringComparison.OrdinalIgnoreCase))
                {
                    mode = UVWrapMode.MIRROR_ONCE;
                    return true;
                }

                // Enum names.
                if (Enum.TryParse<UVWrapMode>(s, ignoreCase: true, out var parsedEnum))
                {
                    mode = parsedEnum;
                    return true;
                }

                mode = default;
                return false;
            }

            private static string WrapModeToString(UVWrapMode mode)
            {
                return mode switch
                {
                    UVWrapMode.WRAP => "Repeat",
                    UVWrapMode.CLAMP => "Clamp",
                    UVWrapMode.MIRROR => "MirroredRepeat",
                    UVWrapMode.MIRROR_ONCE => "MirrorOnce",
                    _ => "Clamp",
                };
            }
        }
    }
}
