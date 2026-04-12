using FlatSharp;
using GFTool.Renderer.Core;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Trinity.Core.Flatbuffers.Anm2;
using Trinity.Core.Utils;

namespace GFTool.Renderer.Scene.GraphicsObjects
{
    public sealed class MaterialParameterAnimation
    {
        private readonly record struct FloatKey(string MaterialName, string AttributeName);

        private sealed class FloatChannel
        {
            public FloatKey Key { get; init; }
            public RuntimeCurveFloat Curve { get; init; } = new RuntimeCurveFloat();
            public bool CastToInt { get; init; }
        }

        private sealed class Float4Channel
        {
            public FloatKey Key { get; init; }
            public RuntimeCurveFloat4 Curve { get; init; } = new RuntimeCurveFloat4();
        }

        private sealed class RestoreEntry
        {
            public bool HadOverride { get; init; }
            public object? Value { get; init; }
        }

        public string Name { get; }
        public string? SourcePath { get; }
        public bool Loops { get; }
        public uint FrameCount { get; }
        public uint FrameRate { get; }

        private readonly FloatChannel[] floatChannels;
        private readonly Float4Channel[] float4Channels;
        private readonly Dictionary<Model, Dictionary<Material, Dictionary<string, RestoreEntry>>> restoreByModel =
            new Dictionary<Model, Dictionary<Material, Dictionary<string, RestoreEntry>>>();

        public static bool TryLoad(string path, out MaterialParameterAnimation? animation)
        {
            animation = null;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            MeshAnimeConfigData cfg;
            try
            {
                cfg = FlatBufferConverter.DeserializeFrom<MeshAnimeConfigData>(path);
            }
            catch
            {
                return false;
            }

            if (cfg?.MeshAnimePaths == null || cfg.MeshAnimePaths.Count == 0)
            {
                return false;
            }

            var frameSetting = cfg.FrameSetting;
            uint fps = frameSetting?.Fps ?? 0;
            uint frames = frameSetting?.FrameLength ?? 0;
            if (fps == 0 || frames == 0)
            {
                return false;
            }

            var name = Path.GetFileNameWithoutExtension(path);
            var created = new MaterialParameterAnimation(cfg, name, path);
            if (created.floatChannels.Length == 0 && created.float4Channels.Length == 0)
            {
                return false;
            }

            animation = created;
            return true;
        }

        public MaterialParameterAnimation(MeshAnimeConfigData cfg, string name, string? sourcePath = null)
        {
            Name = name;
            SourcePath = sourcePath;

            var frameSetting = cfg?.FrameSetting;
            Loops = frameSetting != null && frameSetting.PlayMode == PlayMode.Loop;
            FrameCount = frameSetting?.FrameLength ?? 0;
            FrameRate = frameSetting?.Fps ?? 0;

            var floatList = new List<FloatChannel>();
            var float4List = new List<Float4Channel>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            static string ComposeSeenKey(string materialName, string attributeName, string kind)
            {
                return $"{materialName}\0{attributeName}\0{kind}";
            }

            foreach (var entry in cfg?.MeshAnimePaths ?? Array.Empty<MeshAnimeFilePath>())
            {
                var data = entry?.MaterialData;
                if (data?.MaterialParams == null || data.MaterialParams.Count == 0)
                {
                    continue;
                }

                foreach (var mat in data.MaterialParams)
                {
                    if (mat == null || string.IsNullOrWhiteSpace(mat.MaterialName))
                    {
                        continue;
                    }

                    string materialName = mat.MaterialName;

                    foreach (var p in mat.FloatParams ?? Array.Empty<MaterialParamFloat>())
                    {
                        if (p == null || string.IsNullOrWhiteSpace(p.AttributeName) || p.CurveData == null)
                        {
                            continue;
                        }

                        if (!seen.Add(ComposeSeenKey(materialName, p.AttributeName, "f")))
                        {
                            continue;
                        }

                        floatList.Add(new FloatChannel
                        {
                            Key = new FloatKey(materialName, p.AttributeName),
                            Curve = p.CurveData,
                            CastToInt = false
                        });
                    }

                    foreach (var p in mat.IntParams ?? Array.Empty<MaterialParamFloat>())
                    {
                        if (p == null || string.IsNullOrWhiteSpace(p.AttributeName) || p.CurveData == null)
                        {
                            continue;
                        }

                        if (!seen.Add(ComposeSeenKey(materialName, p.AttributeName, "i")))
                        {
                            continue;
                        }

                        floatList.Add(new FloatChannel
                        {
                            Key = new FloatKey(materialName, p.AttributeName),
                            Curve = p.CurveData,
                            CastToInt = true
                        });
                    }

                    foreach (var p in mat.Float4Params ?? Array.Empty<MaterialParamFloat4>())
                    {
                        if (p == null || string.IsNullOrWhiteSpace(p.AttributeName) || p.CurveData == null)
                        {
                            continue;
                        }

                        if (!seen.Add(ComposeSeenKey(materialName, p.AttributeName, "f4")))
                        {
                            continue;
                        }

                        float4List.Add(new Float4Channel
                        {
                            Key = new FloatKey(materialName, p.AttributeName),
                            Curve = p.CurveData
                        });
                    }
                }
            }

            floatChannels = floatList.ToArray();
            float4Channels = float4List.ToArray();
        }

        public float GetFrame(float timeSeconds, bool forceLoop)
        {
            float frameRate = FrameRate > 0 ? FrameRate : 30f;
            float frame = timeSeconds * frameRate;
            if (FrameCount > 0)
            {
                if (forceLoop || Loops)
                {
                    frame %= FrameCount;
                }
                frame = Math.Clamp(frame, 0f, Math.Max(0f, FrameCount - 1));
            }
            return frame;
        }

        public int FloatChannelCount => floatChannels.Length;

        public int Float4ChannelCount => float4Channels.Length;

        public string[] GetDebugSummaryLines(int maxLines = 10)
        {
            if (maxLines < 1)
            {
                maxLines = 1;
            }

            var lines = new List<string>();
            float endFrame = FrameCount > 0 ? Math.Max(0f, FrameCount - 1) : 0f;
            float f0 = 0f;
            float f1 = Math.Min(1f, endFrame);
            const float eps = 0.00025f;

            for (int i = 0; i < floatChannels.Length && lines.Count < maxLines; i++)
            {
                var c = floatChannels[i];
                float a = SampleCurve(c.Curve, f0);
                float b = SampleCurve(c.Curve, f1);
                float z = SampleCurve(c.Curve, endFrame);
                bool animated = MathF.Abs(a - b) > eps || MathF.Abs(a - z) > eps;
                lines.Add($"[MatAnim] {(animated ? "anim" : "const")} mat='{c.Key.MaterialName}' attr='{c.Key.AttributeName}' v0={a:0.###} vEnd={z:0.###}");
            }

            for (int i = 0; i < float4Channels.Length && lines.Count < maxLines; i++)
            {
                var c = float4Channels[i];
                float a0 = SampleCurve(c.Curve?.X, f0);
                float b0 = SampleCurve(c.Curve?.X, f1);
                float z0 = SampleCurve(c.Curve?.X, endFrame);
                float a1 = SampleCurve(c.Curve?.Y, f0);
                float b1 = SampleCurve(c.Curve?.Y, f1);
                float z1 = SampleCurve(c.Curve?.Y, endFrame);
                float a2 = SampleCurve(c.Curve?.Z, f0);
                float b2 = SampleCurve(c.Curve?.Z, f1);
                float z2 = SampleCurve(c.Curve?.Z, endFrame);
                float a3 = SampleCurve(c.Curve?.W, f0);
                float b3 = SampleCurve(c.Curve?.W, f1);
                float z3 = SampleCurve(c.Curve?.W, endFrame);
                bool animated =
                    MathF.Abs(a0 - b0) > eps || MathF.Abs(a0 - z0) > eps ||
                    MathF.Abs(a1 - b1) > eps || MathF.Abs(a1 - z1) > eps ||
                    MathF.Abs(a2 - b2) > eps || MathF.Abs(a2 - z2) > eps ||
                    MathF.Abs(a3 - b3) > eps || MathF.Abs(a3 - z3) > eps;

                lines.Add(
                    $"[MatAnim] {(animated ? "anim" : "const")} mat='{c.Key.MaterialName}' attr='{c.Key.AttributeName}' " +
                    $"v0=({a0:0.###},{a1:0.###},{a2:0.###},{a3:0.###}) vEnd=({z0:0.###},{z1:0.###},{z2:0.###},{z3:0.###})");
            }

            return lines.ToArray();
        }

        public void ApplyToModel(Model model, float frame)
        {
            if (model == null || (floatChannels.Length == 0 && float4Channels.Length == 0))
            {
                return;
            }

            EnsureRestoreSnapshot(model);

            var materials = model.GetMaterials();
            if (materials == null || materials.Count == 0)
            {
                return;
            }

            var byName = new Dictionary<string, List<Material>>(StringComparer.OrdinalIgnoreCase);
            foreach (var mat in materials)
            {
                if (mat == null || string.IsNullOrWhiteSpace(mat.Name))
                {
                    continue;
                }

                if (!byName.TryGetValue(mat.Name, out var list))
                {
                    list = new List<Material>();
                    byName[mat.Name] = list;
                }
                list.Add(mat);
            }

            for (int i = 0; i < floatChannels.Length; i++)
            {
                var c = floatChannels[i];
                if (!byName.TryGetValue(c.Key.MaterialName, out var list) || list.Count == 0)
                {
                    continue;
                }

                float v = SampleCurve(c.Curve, frame);
                object value = c.CastToInt ? (object)(int)MathF.Round(v) : v;
                for (int m = 0; m < list.Count; m++)
                {
                    list[m].SetUniformOverride(c.Key.AttributeName, value);
                }
            }

            for (int i = 0; i < float4Channels.Length; i++)
            {
                var c = float4Channels[i];
                if (!byName.TryGetValue(c.Key.MaterialName, out var list) || list.Count == 0)
                {
                    continue;
                }

                var v4 = new Vector4(
                    SampleCurve(c.Curve?.X, frame),
                    SampleCurve(c.Curve?.Y, frame),
                    SampleCurve(c.Curve?.Z, frame),
                    SampleCurve(c.Curve?.W, frame));

                for (int m = 0; m < list.Count; m++)
                {
                    list[m].SetUniformOverride(c.Key.AttributeName, v4);
                }
            }
        }

        public void ResetOverridesForModel(Model model)
        {
            if (model == null)
            {
                return;
            }

            if (!restoreByModel.TryGetValue(model, out var byMaterial) || byMaterial == null || byMaterial.Count == 0)
            {
                return;
            }

            foreach (var materialEntry in byMaterial)
            {
                var material = materialEntry.Key;
                var byUniform = materialEntry.Value;
                if (material == null || byUniform == null || byUniform.Count == 0)
                {
                    continue;
                }

                foreach (var u in byUniform)
                {
                    if (u.Value.HadOverride)
                    {
                        material.SetUniformOverride(u.Key, u.Value.Value!);
                    }
                    else
                    {
                        material.ClearUniformOverride(u.Key);
                    }
                }
            }

            restoreByModel.Remove(model);
        }

        private void EnsureRestoreSnapshot(Model model)
        {
            if (restoreByModel.ContainsKey(model))
            {
                return;
            }

            var byMaterial = new Dictionary<Material, Dictionary<string, RestoreEntry>>();
            var materials = model.GetMaterials();
            if (materials != null)
            {
                foreach (var mat in materials)
                {
                    if (mat == null || string.IsNullOrWhiteSpace(mat.Name))
                    {
                        continue;
                    }

                    var map = new Dictionary<string, RestoreEntry>(StringComparer.OrdinalIgnoreCase);

                    void Snapshot(string uniform)
                    {
                        if (string.IsNullOrWhiteSpace(uniform) || map.ContainsKey(uniform))
                        {
                            return;
                        }

                        if (mat.TryGetUniformOverride(uniform, out var value))
                        {
                            map[uniform] = new RestoreEntry { HadOverride = true, Value = value };
                        }
                        else
                        {
                            map[uniform] = new RestoreEntry { HadOverride = false, Value = null };
                        }
                    }

                    for (int i = 0; i < floatChannels.Length; i++)
                    {
                        if (string.Equals(floatChannels[i].Key.MaterialName, mat.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            Snapshot(floatChannels[i].Key.AttributeName);
                        }
                    }

                    for (int i = 0; i < float4Channels.Length; i++)
                    {
                        if (string.Equals(float4Channels[i].Key.MaterialName, mat.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            Snapshot(float4Channels[i].Key.AttributeName);
                        }
                    }

                    if (map.Count > 0)
                    {
                        byMaterial[mat] = map;
                    }
                }
            }

            restoreByModel[model] = byMaterial;
        }

        private static float SampleCurve(RuntimeCurveFloat? curve, float frame)
        {
            if (curve?.Keys == null || curve.Keys.Count == 0)
            {
                return 0.0f;
            }

            var keys = curve.Keys;
            int count = keys.Count;
            if (count == 1 || frame <= keys[0].Time)
            {
                return keys[0].Value;
            }
            if (frame >= keys[count - 1].Time)
            {
                return keys[count - 1].Value;
            }

            for (int i = 0; i < count - 1; i++)
            {
                var k1 = keys[i];
                var k2 = keys[i + 1];
                if (frame < k1.Time || frame > k2.Time)
                {
                    continue;
                }

                float dt = k2.Time - k1.Time;
                if (dt <= 0.000001f)
                {
                    return k2.Value;
                }

                float t = (frame - k1.Time) / dt;
                t = Math.Clamp(t, 0.0f, 1.0f);

                switch (k1.InterpType)
                {
                    case CurveInterpType.Constant:
                        return k1.Value;
                    case CurveInterpType.Linear:
                        return Lerp(k1.Value, k2.Value, t);
                    default:
                        // Hermite using per-key tangents. Tangents are slopes in value-per-frame; scale by dt.
                        float m0 = k1.TangentRight * dt;
                        float m1 = k2.TangentLeft * dt;
                        return Hermite(k1.Value, m0, k2.Value, m1, t);
                }
            }

            return keys[count - 1].Value;
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float Hermite(float p0, float m0, float p1, float m1, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            float h00 = (2 * t3) - (3 * t2) + 1;
            float h10 = t3 - (2 * t2) + t;
            float h01 = (-2 * t3) + (3 * t2);
            float h11 = t3 - t2;
            return (h00 * p0) + (h10 * m0) + (h01 * p1) + (h11 * m1);
        }
    }
}
