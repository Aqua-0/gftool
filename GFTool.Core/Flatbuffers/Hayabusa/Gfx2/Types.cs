using FlatSharp.Attributes;

namespace GFTool.Core.Flatbuffers.Hayabusa.Gfx2
{
    [FlatBufferStruct]
    public class InstanceVector3f
    {
        [FlatBufferItem(0)] public float X { get; set; }
        [FlatBufferItem(1)] public float Y { get; set; }
        [FlatBufferItem(2)] public float Z { get; set; }
    }

    [FlatBufferTable]
    public class InstanceSrt
    {
        [FlatBufferItem(0)] public InstanceVector3f Scale { get; set; }
        [FlatBufferItem(1)] public InstanceVector3f Rotate { get; set; }
        [FlatBufferItem(2)] public InstanceVector3f Translate { get; set; }
    }

    [FlatBufferTable]
    public class InstanceBlob
    {
        [FlatBufferItem(0)] public byte[]? Data { get; set; }
    }
}
