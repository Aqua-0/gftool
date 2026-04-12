using FlatSharp;
using GFTool.Renderer.Core;
using System;
using System.Collections.Generic;
using System.IO;
using Trinity.Core.Flatbuffers.Titan.Animation;

namespace GFTool.Renderer.Scene.GraphicsObjects
{
    public sealed class BlendShapeAnimation
    {
        private sealed record TargetRef(string MeshShapeName, string TargetName);

        private sealed class Channel
        {
            public string Name { get; init; } = string.Empty;
            public FlatBufferUnion<FixedBlendTrack, DynamicBlendTrack, Framed16BlendTrack, Framed8BlendTrack> Track { get; init; }
            public TargetRef[] Targets { get; init; } = Array.Empty<TargetRef>();
        }

        public string Name { get; }
        public string? SourcePath { get; }
        public uint FrameCount { get; }
        public uint FrameRate { get; }
        public byte FrameMultiplier { get; }

        private readonly Channel[] channels;

        [ThreadStatic] private static float sampleFrame;
        [ThreadStatic] private static float sampleBlendResult;

        private static readonly Action SampleDefault = static () => { sampleBlendResult = 0.0f; };
        private static readonly Action<FixedBlendTrack> SampleFixed = static v => { sampleBlendResult = v?.Value ?? 0.0f; };
        private static readonly Action<DynamicBlendTrack> SampleDynamic = static v => { sampleBlendResult = SampleDynamicFloat(v?.Value, sampleFrame); };
        private static readonly Action<Framed16BlendTrack> SampleFramed16 = static v => { sampleBlendResult = SampleFramedFloat16(v?.Frames, v?.Value, sampleFrame); };
        private static readonly Action<Framed8BlendTrack> SampleFramed8 = static v => { sampleBlendResult = SampleFramedFloat8(v?.Frames, v?.Value, sampleFrame); };

        public static BlendShapeAnimation Load(string path)
        {
            var tracm = Trinity.Core.Utils.FlatBufferConverter.DeserializeFrom<TRACM>(path);
            return new BlendShapeAnimation(tracm, Path.GetFileNameWithoutExtension(path), path);
        }

        public BlendShapeAnimation(TRACM tracm, string name, string? sourcePath = null)
        {
            Name = name;
            SourcePath = sourcePath;

            FrameCount = tracm?.Config?.Duration ?? 0;
            FrameRate = tracm?.Config?.Framerate ?? 0;
            FrameMultiplier = tracm?.BlendshapeFrameMultiplier ?? 0;

            var list = new List<Channel>();
            foreach (var t in tracm?.Tracks ?? Array.Empty<Track>())
            {
                if (t == null || !string.Equals(t.TrackPath, "__blendShape", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var timeline = t.BlendshapeAnimation;
                if (timeline?.Res2 == null)
                {
                    continue;
                }

                foreach (var entry in timeline.Res2)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    var targets = new List<TargetRef>();
                    foreach (var init in entry.InitValues ?? Array.Empty<TrackBlendshapeInit>())
                    {
                        if (init == null || string.IsNullOrWhiteSpace(init.MeshName) || init.BlendshapeList == null)
                        {
                            continue;
                        }

                        foreach (var targetName in init.BlendshapeList)
                        {
                            if (string.IsNullOrWhiteSpace(targetName))
                            {
                                continue;
                            }

                            targets.Add(new TargetRef(init.MeshName, targetName));
                        }
                    }

                    if (targets.Count == 0)
                    {
                        continue;
                    }

                    list.Add(new Channel
                    {
                        Name = entry.Name ?? string.Empty,
                        Track = entry.Track,
                        Targets = targets.ToArray()
                    });
                }
            }

            channels = list.ToArray();
        }

        public float GetFrame(float timeSeconds, bool forceLoop)
        {
            float frameRate = FrameRate > 0 ? FrameRate : 30f;
            float frame = timeSeconds * frameRate;
            if (FrameCount > 0)
            {
                if (forceLoop)
                {
                    frame %= FrameCount;
                }
                frame = Math.Clamp(frame, 0f, Math.Max(0f, FrameCount - 1));
            }
            return frame;
        }

        public bool ApplyToModel(Model model, float frame, out string? error)
        {
            error = null;
            if (model == null || channels.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < channels.Length; i++)
            {
                var c = channels[i];
                float w = Math.Clamp(SampleBlend(c.Track, frame), 0.0f, 1.0f);
                var targets = c.Targets;
                for (int j = 0; j < targets.Length; j++)
                {
                    model.SetCpuFullMorphWeight(targets[j].MeshShapeName, targets[j].TargetName, w);
                }
            }

            return model.TryApplyCpuFullMorphs(out error);
        }

        private static float SampleBlend(FlatBufferUnion<FixedBlendTrack, DynamicBlendTrack, Framed16BlendTrack, Framed8BlendTrack> channel, float frame)
        {
            sampleFrame = frame;
            sampleBlendResult = 0.0f;
            channel.Switch(
                defaultCase: SampleDefault,
                case1: SampleFixed,
                case2: SampleDynamic,
                case3: SampleFramed16,
                case4: SampleFramed8
            );
            return sampleBlendResult;
        }

        private static float SampleDynamicFloat(IList<float>? values, float frame)
        {
            if (values == null || values.Count == 0)
            {
                return 0.0f;
            }

            int index = Math.Clamp((int)MathF.Floor(frame), 0, values.Count - 1);
            return values[index];
        }

        private static float SampleFramedFloat16(IList<ushort>? frames, IList<float>? values, float frame)
        {
            if (frames == null || values == null || frames.Count == 0 || values.Count == 0)
            {
                return 0.0f;
            }

            int count = Math.Min(frames.Count, values.Count);
            float keyFrame = frame;
            if (keyFrame <= frames[0]) return values[0];
            if (keyFrame >= frames[count - 1]) return values[count - 1];

            for (int i = 0; i < count - 1; i++)
            {
                float k1 = frames[i];
                float k2 = frames[i + 1];
                if (keyFrame >= k1 && keyFrame <= k2)
                {
                    float denom = k2 - k1;
                    if (denom <= 0.0f)
                    {
                        return values[i + 1];
                    }

                    float t = (keyFrame - k1) / denom;
                    return Lerp(values[i], values[i + 1], t);
                }
            }

            return values[count - 1];
        }

        private static float SampleFramedFloat8(IList<byte>? frames, IList<float>? values, float frame)
        {
            if (frames == null || values == null || frames.Count == 0 || values.Count == 0)
            {
                return 0.0f;
            }

            int count = Math.Min(frames.Count, values.Count);
            float keyFrame = frame;
            if (keyFrame <= frames[0]) return values[0];
            if (keyFrame >= frames[count - 1]) return values[count - 1];

            for (int i = 0; i < count - 1; i++)
            {
                float k1 = frames[i];
                float k2 = frames[i + 1];
                if (keyFrame >= k1 && keyFrame <= k2)
                {
                    float denom = k2 - k1;
                    if (denom <= 0.0f)
                    {
                        return values[i + 1];
                    }

                    float t = (keyFrame - k1) / denom;
                    return Lerp(values[i], values[i + 1], t);
                }
            }

            return values[count - 1];
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * Math.Clamp(t, 0.0f, 1.0f);
        }
    }
}
