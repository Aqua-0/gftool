using FlatSharp.Attributes;

namespace GFTool.Core.Flatbuffers.Hayabusa.Gfx2
{
    // Minimal instance buffer bindings.
    [FlatBufferEnum(typeof(uint))]
    public enum InstanceType : uint
    {
        Unknown = 0,
        Srt = 1,
        Count = 2
    }

    [FlatBufferTable]
    public class InstanceParticle
    {
        [FlatBufferItem(0)] public InstanceSrt? Srt { get; set; }
    }

    [FlatBufferTable]
    public class InstanceBuffer
    {
        [FlatBufferItem(0)] public string? Name { get; set; }
        [FlatBufferItem(1)] public InstanceType Type { get; set; }
        [FlatBufferItem(2)] public int ParticleCount { get; set; }
        [FlatBufferItem(3)] public InstanceParticle[]? ParticleList { get; set; }
        [FlatBufferItem(4)] public InstanceBlob? Buffer { get; set; }
    }
}
