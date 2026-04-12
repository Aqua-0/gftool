using FlatSharp;
using GFTool.Renderer.Core;
using System;
using System.Collections.Generic;
using System.IO;
using Trinity.Core.Flatbuffers.Anm2;
using Trinity.Core.Utils;

namespace GFTool.Renderer.Scene.GraphicsObjects
{
    public sealed class MeshVisibilityAnimation
    {
        private sealed class Channel
        {
            public string MeshName { get; init; } = string.Empty;
            public FlatBufferUnion<OneFrame_Bool, AllFrame_Bool, Step_Bool_u16, Step_Bool_u8> Track { get; init; }
        }

        public string Name { get; }
        public string? SourcePath { get; }
        public bool Loops { get; }
        public uint FrameCount { get; }
        public uint FrameRate { get; }
        public IReadOnlyList<string> ControlledMeshNames => controlledMeshNames;

        private readonly Channel[] channels;
        private readonly List<string> controlledMeshNames = new List<string>();

        [ThreadStatic] private static float sampleFrame;
        [ThreadStatic] private static bool sampleBoolResult;

        private static readonly Action SampleDefault = static () => { sampleBoolResult = true; };
        private static readonly Action<OneFrame_Bool> SampleOneFrame = static v => { sampleBoolResult = v != null && v.Value; };
        private static readonly Action<AllFrame_Bool> SampleAllFrame = static v => { sampleBoolResult = SampleAllFrameBits(v?.ValuesBit, sampleFrame); };
        private static readonly Action<Step_Bool_u16> SampleStep16 = static v => { sampleBoolResult = SampleStepBits(v?.Frames, v?.ValuesBit, sampleFrame); };
        private static readonly Action<Step_Bool_u8> SampleStep8 = static v => { sampleBoolResult = SampleStepBits(v?.Frames, v?.ValuesBit, sampleFrame); };

        public static bool TryLoad(string path, out MeshVisibilityAnimation? animation)
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

            // Sanity: require at least one mesh entry and a plausible frame setting.
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
            animation = new MeshVisibilityAnimation(cfg, name, path);
            return animation.channels.Length > 0;
        }

        public MeshVisibilityAnimation(MeshAnimeConfigData cfg, string name, string? sourcePath = null)
        {
            Name = name;
            SourcePath = sourcePath;

            var frameSetting = cfg?.FrameSetting;
            Loops = frameSetting != null && frameSetting.PlayMode == PlayMode.Loop;
            FrameCount = frameSetting?.FrameLength ?? 0;
            FrameRate = frameSetting?.Fps ?? 0;

            var list = new List<Channel>();
            foreach (var entry in cfg?.MeshAnimePaths ?? Array.Empty<MeshAnimeFilePath>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.MeshName))
                {
                    continue;
                }

                var track = entry.VisibilityData?.VisibilityParam?.Params ?? new FlatBufferUnion<OneFrame_Bool, AllFrame_Bool, Step_Bool_u16, Step_Bool_u8>();
                if (track.Discriminator == 0)
                {
                    continue;
                }

                var meshName = entry.MeshName;
                list.Add(new Channel { MeshName = meshName, Track = track });
                controlledMeshNames.Add(meshName);
            }

            channels = list.ToArray();
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

        public void ApplyToModel(Model model, float frame)
        {
            if (model == null || channels.Length == 0)
            {
                return;
            }

            for (int i = 0; i < channels.Length; i++)
            {
                var c = channels[i];
                bool visible = SampleBool(c.Track, frame);
                model.SetVisibleForMeshShapeName(c.MeshName, visible);
            }
        }

        public void ResetControlledMeshesToVisible(Model model)
        {
            if (model == null || controlledMeshNames.Count == 0)
            {
                return;
            }

            for (int i = 0; i < controlledMeshNames.Count; i++)
            {
                model.SetVisibleForMeshShapeName(controlledMeshNames[i], visible: true);
            }
        }

        private static bool SampleBool(FlatBufferUnion<OneFrame_Bool, AllFrame_Bool, Step_Bool_u16, Step_Bool_u8> channel, float frame)
        {
            sampleFrame = frame;
            sampleBoolResult = true;
            channel.Switch(
                defaultCase: SampleDefault,
                case1: SampleOneFrame,
                case2: SampleAllFrame,
                case3: SampleStep16,
                case4: SampleStep8
            );
            return sampleBoolResult;
        }

        private static bool SampleAllFrameBits(IList<byte>? bits, float frame)
        {
            if (bits == null || bits.Count == 0)
            {
                return true;
            }

            int index = Math.Max(0, (int)MathF.Floor(frame));
            int byteIndex = index >> 3;
            if (byteIndex < 0 || byteIndex >= bits.Count)
            {
                return true;
            }

            int bit = index & 7;
            return ((bits[byteIndex] >> bit) & 1) != 0;
        }

        private static bool SampleStepBits(IList<ushort>? frames, IList<byte>? bits, float frame)
        {
            if (frames == null || bits == null || frames.Count == 0 || bits.Count == 0)
            {
                return true;
            }

            int keyCount = frames.Count;
            int idxFrame = Math.Max(0, (int)MathF.Floor(frame));

            int keyIndex = 0;
            if (idxFrame <= frames[0])
            {
                keyIndex = 0;
            }
            else if (idxFrame >= frames[keyCount - 1])
            {
                keyIndex = keyCount - 1;
            }
            else
            {
                for (int i = 0; i < keyCount - 1; i++)
                {
                    if (idxFrame >= frames[i] && idxFrame < frames[i + 1])
                    {
                        keyIndex = i;
                        break;
                    }
                }
            }

            int byteIndex = keyIndex >> 3;
            if (byteIndex < 0 || byteIndex >= bits.Count)
            {
                return true;
            }

            int bit = keyIndex & 7;
            return ((bits[byteIndex] >> bit) & 1) != 0;
        }

        private static bool SampleStepBits(IList<byte>? frames, IList<byte>? bits, float frame)
        {
            if (frames == null || bits == null || frames.Count == 0 || bits.Count == 0)
            {
                return true;
            }

            int keyCount = frames.Count;
            int idxFrame = Math.Max(0, (int)MathF.Floor(frame));

            int keyIndex = 0;
            if (idxFrame <= frames[0])
            {
                keyIndex = 0;
            }
            else if (idxFrame >= frames[keyCount - 1])
            {
                keyIndex = keyCount - 1;
            }
            else
            {
                for (int i = 0; i < keyCount - 1; i++)
                {
                    if (idxFrame >= frames[i] && idxFrame < frames[i + 1])
                    {
                        keyIndex = i;
                        break;
                    }
                }
            }

            int byteIndex = keyIndex >> 3;
            if (byteIndex < 0 || byteIndex >= bits.Count)
            {
                return true;
            }

            int bit = keyIndex & 7;
            return ((bits[byteIndex] >> bit) & 1) != 0;
        }
    }
}
