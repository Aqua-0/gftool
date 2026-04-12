using FlatSharp.Attributes;

namespace GFTool.Core.Flatbuffers.Hayabusa.Terrain
{
    // Minimal terrain bindings.
    [FlatBufferStruct]
    public class Vector4
    {
        [FlatBufferItem(0)] public float X { get; set; }
        [FlatBufferItem(1)] public float Y { get; set; }
        [FlatBufferItem(2)] public float Z { get; set; }
        [FlatBufferItem(3)] public float W { get; set; }
    }

    [FlatBufferTable]
    public class AABB
    {
        [FlatBufferItem(0)] public Vector4 Min { get; set; }
        [FlatBufferItem(1)] public Vector4 Max { get; set; }
    }

    [FlatBufferTable]
    public class Tree
    {
        [FlatBufferItem(0)] public string? ModelFilePath { get; set; }
        [FlatBufferItem(1)] public string[]? InstanceFilePathList { get; set; }
    }

    [FlatBufferTable]
    public class Terrain
    {
        // Keep indices aligned with the upstream schema. We only need Trees and BoundingBox today.
        [FlatBufferItem(4)] public Tree[]? Trees { get; set; }
        [FlatBufferItem(10)] public AABB? BoundingBox { get; set; }
    }
}
