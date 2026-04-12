using FlatSharp.Attributes;
using GFTool.Core.Flatbuffers.Hayabusa.Placement.Common;

namespace GFTool.Core.Flatbuffers.Hayabusa.Placement.Door
{
    [FlatBufferTable]
    public class door_data
    {
        [FlatBufferItem(0)] public string? DoorID { get; set; }
        [FlatBufferItem(1)] public ulong SystemUniqueID { get; set; }
        [FlatBufferItem(2)] public string? AreaName { get; set; }
        [FlatBufferItem(3)] public string? ExitDoorID { get; set; }
        [FlatBufferItem(4)] public ulong Type { get; set; }
        [FlatBufferItem(5)] public ulong FadeType { get; set; }
        [FlatBufferItem(6)] public string? SoundOpen { get; set; }
        [FlatBufferItem(7)] public string? SoundClose { get; set; }
        [FlatBufferItem(8)] public PlacementVector? ExitPos { get; set; }
        [FlatBufferItem(9)] public float ExitRotY { get; set; }
        [FlatBufferItem(10)] public PlacementVector? ActionButtonPos { get; set; }
        [FlatBufferItem(11)] public bool UsesLookAt { get; set; }
        [FlatBufferItem(12)] public PlacementVector? LookAtPos { get; set; }
        [FlatBufferItem(13)] public PlacementVector? DestinationPos { get; set; }
        [FlatBufferItem(14)] public ulong ExitCameraType { get; set; }
        [FlatBufferItem(15)] public float ExitFieldCameraYawDeg { get; set; }
        [FlatBufferItem(16)] public float ExitFieldCameraPitchDeg { get; set; }
        [FlatBufferItem(17)] public PlacementVector? ExitEventCameraPos { get; set; }
        [FlatBufferItem(18)] public PlacementVector? ExitEventCameraRot { get; set; }
        [FlatBufferItem(19)] public float ExitEventCameraFovYDeg { get; set; }
        [FlatBufferItem(20)] public int ExitEventCameraFixFrame { get; set; }
        [FlatBufferItem(21)] public int ExitEventCameraInterpolationFrame { get; set; }
        [FlatBufferItem(22)] public ulong ExitEventCameraInterpolationType { get; set; }
        [FlatBufferItem(23)] public PlacementBaseParameter[]? BaseParameterArray { get; set; }
        [FlatBufferItem(24)] public ulong Shape { get; set; }
        [FlatBufferItem(25)] public PlacementVector? ColPos { get; set; }
        [FlatBufferItem(26)] public float ColRotY { get; set; }
        [FlatBufferItem(27)] public float Radius { get; set; }
        [FlatBufferItem(28)] public float BoxSizeX { get; set; }
        [FlatBufferItem(29)] public float BoxSizeY { get; set; }
        [FlatBufferItem(30)] public float BoxSizeZ { get; set; }
    }

    [FlatBufferTable]
    public class door_dataParameter
    {
        [FlatBufferItem(0)] public door_data[]? DoorDataArray { get; set; }
    }
}
