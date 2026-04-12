using FlatSharp.Attributes;

namespace GFTool.Core.Flatbuffers.Hayabusa.Placement.Common
{
    // Minimal placement support types.
    [FlatBufferTable]
    public class PlacementVector
    {
        [FlatBufferItem(0)] public float X { get; set; }
        [FlatBufferItem(1)] public float Y { get; set; }
        [FlatBufferItem(2)] public float Z { get; set; }
    }

    [FlatBufferTable]
    public class PlacementBaseParameter
    {
        [FlatBufferItem(0)] public ulong ActiveConditionsHash { get; set; }
        [FlatBufferItem(1)] public ulong Op { get; set; }
        [FlatBufferItem(2)] public string? ActiveConditionsArg1 { get; set; }
        [FlatBufferItem(3)] public string? ActiveConditionsArg2 { get; set; }
        [FlatBufferItem(4)] public string? ActiveConditionsArg3 { get; set; }
        [FlatBufferItem(5)] public string? ActiveConditionsArg4 { get; set; }
        [FlatBufferItem(6)] public string? ActiveConditionsArg5 { get; set; }
        [FlatBufferItem(7)] public PlacementVector? Pos { get; set; }
        [FlatBufferItem(8)] public PlacementVector? Rot { get; set; }
        [FlatBufferItem(9)] public PlacementVector? Scale { get; set; }
    }
}
