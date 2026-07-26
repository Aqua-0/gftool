using FlatSharp.Attributes;
using Trinity.Core.Flatbuffers.Utils;

namespace GFTool.Core.Flatbuffers.TR.Scene.Components
{
    [FlatBufferEnum(typeof(byte))]
    public enum CombineLodDistanceType : byte
    {
        Circle = 0,
        Box = 1
    }

    [FlatBufferEnum(typeof(ushort))]
    public enum CombineLodUpdateInterval : ushort
    {
        Every1Frame = 0,
        Every2Frames = 1,
        Every3Frames = 2,
        Every4Frames = 3,
        Every5Frames = 4,
        Every6Frames = 5,
        Every10Frames = 6,
        Every12Frames = 7,
        Every15Frames = 8,
        Every30Frames = 9,
        Every60Frames = 10
    }

    [FlatBufferTable]
    public class CombineLODConfigure
    {
        [FlatBufferItem(0)]
        public string? ModelFilePath { get; set; }

        [FlatBufferItem(1)]
        public string? AnimationFilePath { get; set; }

        [FlatBufferItem(2)]
        public string? ScriptPackageName { get; set; }
    }

    [FlatBufferTable]
    public class trinity_CombineLODEntity
    {
        [FlatBufferItem(0)]
        public string? ResourceName { get; set; }

        [FlatBufferItem(1)]
        public string[]? TargetObjectNames { get; set; }

        [FlatBufferItem(2)]
        public Vector3f? ReferencePoint { get; set; }

        [FlatBufferItem(3)]
        public float CombineDistance { get; set; }

        [FlatBufferItem(4)]
        public float ReleaseDistance { get; set; }

        [FlatBufferItem(5)]
        public CombineLODConfigure[]? Configures { get; set; }

        [FlatBufferItem(6)]
        public CombineLodDistanceType DistanceType { get; set; }

        [FlatBufferItem(7)]
        public bool TargetAll { get; set; }

        [FlatBufferItem(8)]
        public Vector3f? Position { get; set; }

        [FlatBufferItem(9)]
        public Vector3f? Rotation { get; set; }

        [FlatBufferItem(10)]
        public float EnableDistance { get; set; }

        [FlatBufferItem(11)]
        public bool SyncConditionalStreaming { get; set; }

        [FlatBufferItem(12)]
        public CombineLodUpdateInterval UpdateInterval { get; set; }
    }
}
