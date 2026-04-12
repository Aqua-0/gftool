using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GFTool.Renderer.Scene.GraphicsObjects;
using OpenTK.Mathematics;
using Trinity.Core.Flatbuffers.TR.Model;
using Trinity.Core.Flatbuffers.Utils;
using Trinity.Core.Utils;

namespace TrinityModelViewer.Export
{
    internal static class TrmtrReserializePatcher
    {
        public static byte[] BuildEditedTrmtrBytes(TrmtrFile trmtr, Model model)
        {
            if (trmtr == null) throw new ArgumentNullException(nameof(trmtr));
            if (model == null) throw new ArgumentNullException(nameof(model));
            ApplyRuntimeEdits(trmtr, model);
            return FlatBufferConverter.SerializeFrom(trmtr);
        }

        public static void PatchTrmtrInPlaceByReserialize(string trmtrPath, Model model)
        {
            if (string.IsNullOrWhiteSpace(trmtrPath)) throw new ArgumentException("Missing TRMTR path.", nameof(trmtrPath));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (!File.Exists(trmtrPath)) throw new FileNotFoundException("TRMTR not found.", trmtrPath);

            var src = FlatBufferConverter.DeserializeFrom<TrmtrFile>(trmtrPath);
            var bytes = BuildEditedTrmtrBytes(src, model);
            File.WriteAllBytes(trmtrPath, bytes);
        }

        public static void ExportEditedTrmtrByReserialize(string sourceTrmtrPath, Model model, string outputTrmtrPath)
        {
            if (string.IsNullOrWhiteSpace(sourceTrmtrPath)) throw new ArgumentException("Missing source TRMTR path.", nameof(sourceTrmtrPath));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (string.IsNullOrWhiteSpace(outputTrmtrPath)) throw new ArgumentException("Missing output TRMTR path.", nameof(outputTrmtrPath));
            if (!File.Exists(sourceTrmtrPath)) throw new FileNotFoundException("Source TRMTR not found.", sourceTrmtrPath);

            var src = FlatBufferConverter.DeserializeFrom<TrmtrFile>(sourceTrmtrPath);
            var bytes = BuildEditedTrmtrBytes(src, model);
            Directory.CreateDirectory(Path.GetDirectoryName(outputTrmtrPath) ?? ".");
            File.WriteAllBytes(outputTrmtrPath, bytes);
        }

        private static void ApplyRuntimeEdits(TrmtrFile trmtr, Model model)
        {
            if (trmtr?.Materials == null || trmtr.Materials.Length == 0)
            {
                return;
            }

            ApplyNewMaterialClones(trmtr, model);

            var runtimeByName = model.GetMaterials()
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Name))
                .ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var mat in trmtr.Materials)
            {
                if (mat == null || string.IsNullOrWhiteSpace(mat.Name) || !runtimeByName.TryGetValue(mat.Name, out var runtime))
                {
                    continue;
                }

                var overrides = runtime.GetUniformOverridesSnapshot();
                if (overrides.Length > 0)
                {
                    ApplyUniformOverrides(mat, overrides);
                }

                ApplySamplerOverrides(mat, runtime.Samplers);
            }
        }

        private static void ApplyNewMaterialClones(TrmtrFile trmtr, Model model)
        {
            if (trmtr.Materials == null || trmtr.Materials.Length == 0)
            {
                return;
            }

            var requests = model.GetNewMaterialCloneRequestsSnapshot();
            if (requests == null || requests.Count == 0)
            {
                return;
            }

            var existingNames = new HashSet<string>(
                trmtr.Materials.Where(m => m != null && !string.IsNullOrWhiteSpace(m.Name)).Select(m => m.Name),
                StringComparer.OrdinalIgnoreCase);

            var materials = trmtr.Materials.ToList();
            foreach (var req in requests)
            {
                if (string.IsNullOrWhiteSpace(req.NewName) || string.IsNullOrWhiteSpace(req.TemplateName))
                {
                    continue;
                }

                if (existingNames.Contains(req.NewName))
                {
                    continue;
                }

                var template = materials.FirstOrDefault(m => m != null && string.Equals(m.Name, req.TemplateName, StringComparison.OrdinalIgnoreCase));
                if (template == null)
                {
                    continue;
                }

                var clone = CloneMaterial(template);
                clone.Name = req.NewName;
                materials.Add(clone);
                existingNames.Add(req.NewName);
            }

            trmtr.Materials = materials.ToArray();
        }

        private static TrmtrFileMaterial CloneMaterial(TrmtrFileMaterial src)
        {
            var dst = new TrmtrFileMaterial
            {
                Name = src.Name ?? string.Empty,
                BlendStatePreset = src.BlendStatePreset ?? string.Empty,
                CastShadow = src.CastShadow,
                ReceiveShadow = src.ReceiveShadow
            };

            dst.DepthState = src.DepthState == null
                ? null
                : new TrmtrFileDepthState
                {
                    DepthTestEnable = src.DepthState.DepthTestEnable,
                    DepthWriteEnable = src.DepthState.DepthWriteEnable
                };

            dst.RasterizationState = src.RasterizationState == null
                ? null
                : new TrmtrFileRasterizationState
                {
                    FrontFace = src.RasterizationState.FrontFace,
                    CullMode = src.RasterizationState.CullMode,
                    DepthBias = src.RasterizationState.DepthBias,
                    SlopeScaledDepthBias = src.RasterizationState.SlopeScaledDepthBias,
                    DepthBiasClamp = src.RasterizationState.DepthBiasClamp
                };

            dst.Shaders = (src.Shaders ?? Array.Empty<TrmtrFileShader>())
                .Select(s => s == null
                    ? null!
                    : new TrmtrFileShader
                    {
                        Name = s.Name ?? string.Empty,
                        Values = (s.Values ?? Array.Empty<TrmtrFileStringParameter>())
                            .Select(p => p == null ? null! : new TrmtrFileStringParameter { Name = p.Name ?? string.Empty, Value = p.Value ?? string.Empty })
                            .ToArray()
                    })
                .Where(s => s != null)
                .ToArray();

            dst.Textures = (src.Textures ?? Array.Empty<TrmtrFileTexture>())
                .Select(t => t == null ? null! : new TrmtrFileTexture { Name = t.Name ?? string.Empty, File = t.File ?? string.Empty, Slot = t.Slot })
                .Where(t => t != null)
                .ToArray();

            dst.Samplers = (src.Samplers ?? Array.Empty<TrmtrFileSamplerState>())
                .Select(s => s == null
                    ? null!
                    : new TrmtrFileSamplerState
                    {
                        State0 = s.State0,
                        State1 = s.State1,
                        State2 = s.State2,
                        State3 = s.State3,
                        State4 = s.State4,
                        State5 = s.State5,
                        State6 = s.State6,
                        State7 = s.State7,
                        State8 = s.State8,
                        RepeatU = s.RepeatU,
                        RepeatV = s.RepeatV,
                        RepeatW = s.RepeatW,
                        BorderColor = s.BorderColor == null
                            ? null
                            : new Trinity.Core.Flatbuffers.Utils.RGBA
                            {
                                R = s.BorderColor.R,
                                G = s.BorderColor.G,
                                B = s.BorderColor.B,
                                A = s.BorderColor.A
                            }
                    })
                .Where(s => s != null)
                .ToArray();

            dst.FloatParameters = (src.FloatParameters ?? Array.Empty<TrmtrFileFloatParameter>())
                .Select(p => p == null ? null! : new TrmtrFileFloatParameter { Name = p.Name ?? string.Empty, Value = p.Value })
                .Where(p => p != null)
                .ToArray();

            dst.IntParameters = (src.IntParameters ?? Array.Empty<TrmtrFileIntParameter>())
                .Select(p => p == null ? null! : new TrmtrFileIntParameter { Name = p.Name ?? string.Empty, Value = p.Value })
                .Where(p => p != null)
                .ToArray();

            dst.Vector2fParameters = (src.Vector2fParameters ?? Array.Empty<TrmtrFileVector2fParameter>())
                .Select(p => p == null || p.Value == null
                    ? null!
                    : new TrmtrFileVector2fParameter
                    {
                        Name = p.Name ?? string.Empty,
                        Value = new Trinity.Core.Flatbuffers.Utils.Vector2f { X = p.Value.X, Y = p.Value.Y }
                    })
                .Where(p => p != null)
                .ToArray();

            dst.Vector3fParameters = (src.Vector3fParameters ?? Array.Empty<TrmtrFileVector3fParameter>())
                .Select(p => p == null || p.Value == null
                    ? null!
                    : new TrmtrFileVector3fParameter
                    {
                        Name = p.Name ?? string.Empty,
                        Value = new Trinity.Core.Flatbuffers.Utils.Vector3f { X = p.Value.X, Y = p.Value.Y, Z = p.Value.Z }
                    })
                .Where(p => p != null)
                .ToArray();

            dst.Float4Parameters = (src.Float4Parameters ?? Array.Empty<TrmtrFileFloat4Parameter>())
                .Select(p => p == null || p.Value == null
                    ? null!
                    : new TrmtrFileFloat4Parameter
                    {
                        Name = p.Name ?? string.Empty,
                        Value = new Trinity.Core.Flatbuffers.Utils.RGBA
                        {
                            R = p.Value.R,
                            G = p.Value.G,
                            B = p.Value.B,
                            A = p.Value.A
                        }
                    })
                .Where(p => p != null)
                .ToArray();

            dst.Matrix4x4fParameters = (src.Matrix4x4fParameters ?? Array.Empty<TrmtrFileMatrix4x4fParameter>())
                .Select(p => p == null || p.Value == null
                    ? null!
                    : new TrmtrFileMatrix4x4fParameter
                    {
                        Name = p.Name ?? string.Empty,
                        Value = new TrmtrFileMatrix4x4f
                        {
                            Row0 = p.Value.Row0 == null
                                ? new Trinity.Core.Flatbuffers.Utils.Vector4f()
                                : new Trinity.Core.Flatbuffers.Utils.Vector4f { W = p.Value.Row0.W, X = p.Value.Row0.X, Y = p.Value.Row0.Y, Z = p.Value.Row0.Z },
                            Row1 = p.Value.Row1 == null
                                ? new Trinity.Core.Flatbuffers.Utils.Vector4f()
                                : new Trinity.Core.Flatbuffers.Utils.Vector4f { W = p.Value.Row1.W, X = p.Value.Row1.X, Y = p.Value.Row1.Y, Z = p.Value.Row1.Z },
                            Row2 = p.Value.Row2 == null
                                ? new Trinity.Core.Flatbuffers.Utils.Vector4f()
                                : new Trinity.Core.Flatbuffers.Utils.Vector4f { W = p.Value.Row2.W, X = p.Value.Row2.X, Y = p.Value.Row2.Y, Z = p.Value.Row2.Z },
                            Row3 = p.Value.Row3 == null
                                ? new Trinity.Core.Flatbuffers.Utils.Vector4f()
                                : new Trinity.Core.Flatbuffers.Utils.Vector4f { W = p.Value.Row3.W, X = p.Value.Row3.X, Y = p.Value.Row3.Y, Z = p.Value.Row3.Z }
                        }
                    })
                .Where(p => p != null)
                .ToArray();

            dst.Vector2iParameters = (src.Vector2iParameters ?? Array.Empty<TrmtrFileVector2iParameter>())
                .Select(p => p == null || p.Value == null
                    ? null!
                    : new TrmtrFileVector2iParameter
                    {
                        Name = p.Name ?? string.Empty,
                        Value = new TrmtrFileVector2i { X = p.Value.X, Y = p.Value.Y }
                    })
                .Where(p => p != null)
                .ToArray();

            dst.Vector3iParameters = (src.Vector3iParameters ?? Array.Empty<TrmtrFileVector3iParameter>())
                .Select(p => p == null || p.Value == null
                    ? null!
                    : new TrmtrFileVector3iParameter
                    {
                        Name = p.Name ?? string.Empty,
                        Value = new TrmtrFileVector3i { X = p.Value.X, Y = p.Value.Y, Z = p.Value.Z }
                    })
                .Where(p => p != null)
                .ToArray();

            dst.Vector4iParameters = (src.Vector4iParameters ?? Array.Empty<TrmtrFileVector4iParameter>())
                .Select(p => p == null || p.Value == null
                    ? null!
                    : new TrmtrFileVector4iParameter
                    {
                        Name = p.Name ?? string.Empty,
                        Value = new TrmtrFileVector4i { X = p.Value.X, Y = p.Value.Y, Z = p.Value.Z, W = p.Value.W }
                    })
                .Where(p => p != null)
                .ToArray();

            return dst;
        }

        private static void ApplySamplerOverrides(TrmtrFileMaterial dst, IReadOnlyList<TRSampler> srcSamplers)
        {
            if (dst.Samplers == null || dst.Samplers.Length == 0 || srcSamplers == null || srcSamplers.Count == 0)
            {
                return;
            }

            int count = Math.Min(dst.Samplers.Length, srcSamplers.Count);
            for (int i = 0; i < count; i++)
            {
                var s = srcSamplers[i];
                var d = dst.Samplers[i];
                if (s == null || d == null)
                {
                    continue;
                }

                d.RepeatU = s.RepeatU;
                d.RepeatV = s.RepeatV;
                d.RepeatW = s.RepeatW;
            }
        }

        private static void ApplyUniformOverrides(TrmtrFileMaterial dst, KeyValuePair<string, object>[] overrides)
        {
            var byName = overrides
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            // Shader options: update existing entries.
            foreach (var shader in dst.Shaders ?? Array.Empty<TrmtrFileShader>())
            {
                foreach (var p in shader?.Values ?? Array.Empty<TrmtrFileStringParameter>())
                {
                    if (p == null || string.IsNullOrWhiteSpace(p.Name))
                    {
                        continue;
                    }

                    if (!byName.TryGetValue(p.Name, out var v))
                    {
                        continue;
                    }

                    p.Value = ConvertOptionChoiceToString(v, p.Value ?? string.Empty);
                }
            }

            // Floats.
            foreach (var p in dst.FloatParameters ?? Array.Empty<TrmtrFileFloatParameter>())
            {
                if (p == null || string.IsNullOrWhiteSpace(p.Name) || !byName.TryGetValue(p.Name, out var v) || !TryConvertFloat(v, out var f))
                {
                    continue;
                }
                p.Value = f;
            }

            // Ints.
            foreach (var p in dst.IntParameters ?? Array.Empty<TrmtrFileIntParameter>())
            {
                if (p == null || string.IsNullOrWhiteSpace(p.Name) || !byName.TryGetValue(p.Name, out var v) || !TryConvertInt(v, out var n))
                {
                    continue;
                }
                p.Value = n;
            }

            // Float4 (RGBA in file; runtime uses Vector4 x/y/z/w).
            void ApplyFloat4Params(TrmtrFileFloat4Parameter[]? list)
            {
                if (list == null || list.Length == 0)
                {
                    return;
                }

                foreach (var p in list)
                {
                    if (p == null || string.IsNullOrWhiteSpace(p.Name) || p.Value == null || !byName.TryGetValue(p.Name, out var v) || v is not Vector4 vec)
                    {
                        continue;
                    }

                    p.Value.R = vec.X;
                    p.Value.G = vec.Y;
                    p.Value.B = vec.Z;
                    p.Value.A = vec.W;
                }
            }

            ApplyFloat4Params(dst.Float4Parameters);
        }

        private static string ConvertOptionChoiceToString(object value, string existingChoice)
        {
            if (value == null)
            {
                return string.Empty;
            }

            switch (value)
            {
                case string s:
                    if ((string.Equals(existingChoice, "true", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(existingChoice, "false", StringComparison.OrdinalIgnoreCase)) &&
                        (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)))
                    {
                        return ConvertOptionChoiceToString(string.Equals(s, "true", StringComparison.OrdinalIgnoreCase), existingChoice);
                    }
                    return s;
                case bool b:
                    if (string.Equals(existingChoice, "true", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(existingChoice, "false", StringComparison.OrdinalIgnoreCase))
                    {
                        bool upper = existingChoice.Length > 0 && char.IsUpper(existingChoice[0]);
                        return upper
                            ? (b ? "True" : "False")
                            : (b ? "true" : "false");
                    }
                    return b ? "1" : "0";
                case int i:
                    return i.ToString(CultureInfo.InvariantCulture);
                case float f:
                    return f.ToString("0.####", CultureInfo.InvariantCulture);
                case double d:
                    return d.ToString("0.####", CultureInfo.InvariantCulture);
                default:
                    return value.ToString() ?? string.Empty;
            }
        }

        private static bool TryConvertFloat(object value, out float f)
        {
            switch (value)
            {
                case float vf:
                    f = vf;
                    return true;
                case double vd:
                    f = (float)vd;
                    return true;
                case int vi:
                    f = vi;
                    return true;
                case string s when float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var fs):
                    f = fs;
                    return true;
                default:
                    f = 0f;
                    return false;
            }
        }

        private static bool TryConvertInt(object value, out int n)
        {
            switch (value)
            {
                case int vi:
                    n = vi;
                    return true;
                case float vf:
                    n = (int)MathF.Round(vf);
                    return true;
                case double vd:
                    n = (int)Math.Round(vd);
                    return true;
                case bool vb:
                    n = vb ? 1 : 0;
                    return true;
                case string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var si):
                    n = si;
                    return true;
                case string s when float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var sf):
                    n = (int)MathF.Round(sf);
                    return true;
                default:
                    n = 0;
                    return false;
            }
        }
    }
}
