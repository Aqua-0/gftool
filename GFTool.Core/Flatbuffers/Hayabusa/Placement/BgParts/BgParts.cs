using FlatSharp.Attributes;
using GFTool.Core.Flatbuffers.Hayabusa.Placement.Common;

namespace GFTool.Core.Flatbuffers.Hayabusa.Placement.BgParts
{
    [FlatBufferTable]
    public class bg_parts
    {
        [FlatBufferItem(0)] public string? UniqueID { get; set; }
        [FlatBufferItem(1)] public ulong SystemUniqueID { get; set; }
        [FlatBufferItem(2)] public ulong ModelIDHash { get; set; }
        [FlatBufferItem(3)] public bool IsImmortal { get; set; }
        [FlatBufferItem(4)] public PlacementBaseParameter[]? BaseParameterArray { get; set; }
        [FlatBufferItem(5)] public float SpaceRadius { get; set; }
        [FlatBufferItem(6)] public bool CanSurf { get; set; }
        [FlatBufferItem(7)] public bool CanWallClimb { get; set; }
        [FlatBufferItem(8)] public bool CanDig { get; set; }
        [FlatBufferItem(9)] public bool IsShallowWater { get; set; }
        [FlatBufferItem(10)] public bool NonCollision { get; set; }
        [FlatBufferItem(11)] public bool AlwaysSliding { get; set; }
        [FlatBufferItem(12)] public bool AlwaysSupported { get; set; }
    }

    [FlatBufferTable]
    public class bg_partsParameter
    {
        [FlatBufferItem(0)] public bg_parts[]? BgPartsArray { get; set; }
    }
}
