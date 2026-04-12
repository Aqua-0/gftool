using FlatSharp.Attributes;

namespace GFTool.Core.Flatbuffers.Hayabusa.Physics
{
    // Minimal heightfield bindings.
    [FlatBufferTable]
    public class HeightFieldVector
    {
        [FlatBufferItem(0)] public float X { get; set; }
        [FlatBufferItem(1)] public float Y { get; set; }
        [FlatBufferItem(2)] public float Z { get; set; }
    }

    [FlatBufferTable]
    public class HeightFieldLayer
    {
        [FlatBufferItem(0)] public string? Name { get; set; }
        [FlatBufferItem(1)] public byte[]? Pixels { get; set; }
    }

    [FlatBufferTable]
    public class HeightFieldMask
    {
        [FlatBufferItem(0)] public byte[]? Pixels { get; set; }
    }

    [FlatBufferTable]
    public class TrhfHeightField
    {
        [FlatBufferItem(0)] public HeightFieldVector? Position { get; set; }
        [FlatBufferItem(1)] public HeightFieldVector? Scale { get; set; }
        [FlatBufferItem(2)] public HeightFieldVector? Size { get; set; }
        [FlatBufferItem(3)] public int Row { get; set; }
        [FlatBufferItem(4)] public int Column { get; set; }
        [FlatBufferItem(5)] public float[]? Heights { get; set; }
        [FlatBufferItem(6)] public int LayerWidth { get; set; }
        [FlatBufferItem(7)] public int LayerHeight { get; set; }
        [FlatBufferItem(8)] public HeightFieldLayer[]? Layers { get; set; }
        [FlatBufferItem(9)] public HeightFieldMask? Mask { get; set; }
    }
}
