using FlatSharp.Attributes;

namespace GFTool.Core.Flatbuffers.Hayabusa.Placement.BgParts
{
    [FlatBufferTable]
    public class bg_parts_model_set
    {
        [FlatBufferItem(0)] public ulong ModelIDHash { get; set; }
        [FlatBufferItem(1)] public string? TrmdlPath { get; set; }
        [FlatBufferItem(2)] public string? TrcolPath { get; set; }
        [FlatBufferItem(3)] public int[]? ModelLod { get; set; }
        [FlatBufferItem(4)] public string? LodTableType { get; set; }
        [FlatBufferItem(5)] public string? TracnPath { get; set; }
        [FlatBufferItem(6)] public bool NonOccluder { get; set; }
        [FlatBufferItem(7)] public bool IsHollow { get; set; }
        [FlatBufferItem(8)] public string? PackTrmdlPath { get; set; }
        [FlatBufferItem(9)] public string? PackTracnPath { get; set; }
        [FlatBufferItem(10)] public string? PackTrcolPath { get; set; }
        [FlatBufferItem(11)] public bool NonCollision { get; set; }
        [FlatBufferItem(12)] public bool AlwaysSliding { get; set; }
        [FlatBufferItem(13)] public bool AlwaysSupported { get; set; }
    }

    [FlatBufferTable]
    public class bg_parts_model_setParameter
    {
        [FlatBufferItem(0)] public bg_parts_model_set[]? BgPartsModelSetArray { get; set; }
    }
}
