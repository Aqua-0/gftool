using System.Collections.Generic;
using FlatSharp;
using FlatSharp.Attributes;

namespace Trinity.Core.Flatbuffers.Anm2
{
    // Minimal ANM2 visibility animation bindings used by TSV.

    [FlatBufferEnum(typeof(uint))]
    public enum PlayMode : uint
    {
        Once = 0,
        Loop = 1
    }

    [FlatBufferTable]
    public class FrameSetting
    {
        [FlatBufferItem(0)] public PlayMode PlayMode { get; set; }
        [FlatBufferItem(1)] public uint FrameLength { get; set; }
        [FlatBufferItem(2)] public uint Fps { get; set; }
    }

    [FlatBufferTable]
    public class OneFrame_Bool
    {
        [FlatBufferItem(0)] public bool Value { get; set; }
    }

    [FlatBufferTable]
    public class AllFrame_Bool
    {
        // 1 frame per bit (packed).
        [FlatBufferItem(0)] public IList<byte> ValuesBit { get; set; } = new List<byte>();
    }

    [FlatBufferTable]
    public class Step_Bool_u16
    {
        [FlatBufferItem(0)] public IList<ushort> Frames { get; set; } = new List<ushort>();
        // 1 key per bit (packed).
        [FlatBufferItem(1)] public IList<byte> ValuesBit { get; set; } = new List<byte>();
    }

    [FlatBufferTable]
    public class Step_Bool_u8
    {
        [FlatBufferItem(0)] public IList<byte> Frames { get; set; } = new List<byte>();
        // 1 key per bit (packed).
        [FlatBufferItem(1)] public IList<byte> ValuesBit { get; set; } = new List<byte>();
    }

    [FlatBufferTable]
    public class VisibilityParam
    {
        [FlatBufferItem(0)]
        public FlatBufferUnion<OneFrame_Bool, AllFrame_Bool, Step_Bool_u16, Step_Bool_u8> Params { get; set; }
    }

    [FlatBufferTable]
    public class VisibilityMeshAnimationData
    {
        // FrameSetting/editorVisibilityParam are intentionally omitted; only runtime bool keys are needed.
        [FlatBufferItem(2)] public VisibilityParam VisibilityParam { get; set; } = new VisibilityParam();
    }

    [FlatBufferTable]
    public class MeshAnimeFilePath
    {
        [FlatBufferItem(0)] public string MeshName { get; set; } = string.Empty;
        [FlatBufferItem(4)] public MaterialAnimationData MaterialData { get; set; } = new MaterialAnimationData();
        [FlatBufferItem(5)] public VisibilityMeshAnimationData VisibilityData { get; set; } = new VisibilityMeshAnimationData();
        // blendShapeData is index 6; omitted.
    }

    [FlatBufferTable]
    public class MeshAnimeConfigData
    {
        [FlatBufferItem(0)] public FrameSetting FrameSetting { get; set; } = new FrameSetting();
        [FlatBufferItem(1)] public IList<MeshAnimeFilePath> MeshAnimePaths { get; set; } = new List<MeshAnimeFilePath>();
        [FlatBufferItem(2)] public byte MaterialAnimeCount { get; set; }
        [FlatBufferItem(3)] public byte VisibilityAnimeCount { get; set; }
        [FlatBufferItem(4)] public byte BlendShapeAnimeCount { get; set; }
    }
}
