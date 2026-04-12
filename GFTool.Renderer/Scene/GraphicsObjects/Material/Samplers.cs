using System;
using System.Collections.Generic;
using System.Linq;
using Trinity.Core.Flatbuffers.TR.Model;

namespace GFTool.Renderer.Scene.GraphicsObjects
{
    public partial class Material
    {
        private bool hasSamplerOverrides;

        public bool HasSamplerOverrides => hasSamplerOverrides;

        public bool TrySetSamplerWrap(uint samplerId, UVWrapMode repeatU, UVWrapMode repeatV, UVWrapMode? repeatW = null)
        {
            if (samplerId > int.MaxValue)
            {
                return false;
            }

            int id = (int)samplerId;
            if (samplers == null || samplers.Length == 0 || id < 0 || id >= samplers.Length)
            {
                return false;
            }

            var sampler = samplers[id];
            if (sampler == null)
            {
                sampler = new TRSampler();
                samplers[id] = sampler;
            }

            bool changed =
                sampler.RepeatU != repeatU ||
                sampler.RepeatV != repeatV ||
                (repeatW.HasValue && sampler.RepeatW != repeatW.Value);

            if (!changed)
            {
                return false;
            }

            sampler.RepeatU = repeatU;
            sampler.RepeatV = repeatV;
            if (repeatW.HasValue)
            {
                sampler.RepeatW = repeatW.Value;
            }

            hasSamplerOverrides = true;
            RebuildTexturesFromCurrentState();
            return true;
        }

        private void RebuildTexturesFromCurrentState()
        {
            var samplersBySlot = new Dictionary<uint, TRSampler>();
            TRSampler[] samplerArray = samplers ?? Array.Empty<TRSampler>();
            for (int i = 0; i < samplerArray.Length; i++)
            {
                var sampler = samplerArray[i];
                if (sampler != null)
                {
                    samplersBySlot[(uint)i] = sampler;
                }
            }

            var oldTextures = textures ?? new List<Texture>();
            var newTextures = new List<Texture>(oldTextures.Count);

            foreach (var old in oldTextures)
            {
                if (old == null)
                {
                    continue;
                }

                var trTex = new TRTexture
                {
                    Name = old.Name,
                    File = old.SourceFile,
                    Slot = old.Slot
                };

                samplersBySlot.TryGetValue(trTex.Slot, out var sampler);
                newTextures.Add(new Texture(modelpath, trTex, sampler, assetProvider));
            }

            foreach (var old in oldTextures)
            {
                old?.Dispose();
            }

            textures = newTextures;
        }
    }
}
