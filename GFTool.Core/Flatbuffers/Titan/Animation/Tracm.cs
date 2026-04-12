using System.Collections.Generic;
using FlatSharp.Attributes;

namespace Trinity.Core.Flatbuffers.Titan.Animation
{
    // Minimal TRACM bindings used for animation timelines.

    [FlatBufferTable]
    public class TRACM
    {
        [FlatBufferItem(0)] public TrackConfig Config { get; set; } = new TrackConfig();
        [FlatBufferItem(1)] public IList<Track> Tracks { get; set; } = new List<Track>();
        [FlatBufferItem(2)] public byte MaterialFrameMultiplier { get; set; }
        [FlatBufferItem(3)] public byte VisibilityFrameMultiplier { get; set; }
        [FlatBufferItem(4)] public byte BlendshapeFrameMultiplier { get; set; }
    }

    [FlatBufferTable]
    public class TrackConfig
    {
        [FlatBufferItem(0)] public uint Res0 { get; set; }
        [FlatBufferItem(1)] public uint Duration { get; set; }
        [FlatBufferItem(2)] public uint Framerate { get; set; }
    }

    [FlatBufferTable]
    public class Track
    {
        [FlatBufferItem(0)] public string TrackPath { get; set; } = string.Empty;
        [FlatBufferItem(1)] public uint Res1 { get; set; }
        [FlatBufferItem(2)] public uint Res2 { get; set; }
        [FlatBufferItem(3)] public uint Res3 { get; set; }
        [FlatBufferItem(4)] public TrackMaterialTimeline MaterialAnimation { get; set; } = new TrackMaterialTimeline();
        [FlatBufferItem(5)] public VisibilityShapeTimeline VisibilityAnimation { get; set; } = new VisibilityShapeTimeline();
        [FlatBufferItem(6)] public BlendShapeTimeline BlendshapeAnimation { get; set; } = new BlendShapeTimeline();
    }

    // -------------------------
    // Visibility
    // -------------------------

    [FlatBufferTable]
    public class TrackFlagsInfo
    {
        [FlatBufferItem(0)]
        public FlatSharp.FlatBufferUnion<FixedBoolTrack, DynamicBoolTrack, Framed16BoolTrack, Framed8BoolTrack> Values { get; set; }
    }

    [FlatBufferTable]
    public class FixedBoolTrack
    {
        [FlatBufferItem(0)] public bool Value { get; set; }
    }

    [FlatBufferTable]
    public class DynamicBoolTrack
    {
        [FlatBufferItem(0)] public IList<bool> Value { get; set; } = new List<bool>();
    }

    [FlatBufferTable]
    public class Framed16BoolTrack
    {
        [FlatBufferItem(0)] public IList<ushort> Frames { get; set; } = new List<ushort>();
        [FlatBufferItem(1)] public IList<bool> Value { get; set; } = new List<bool>();
    }

    [FlatBufferTable]
    public class Framed8BoolTrack
    {
        [FlatBufferItem(0)] public IList<byte> Frames { get; set; } = new List<byte>();
        [FlatBufferItem(1)] public IList<bool> Value { get; set; } = new List<bool>();
    }

    [FlatBufferTable]
    public class VisibilityShapeTimeline
    {
        [FlatBufferItem(0)] public float Time { get; set; }
        [FlatBufferItem(1)] public float Value { get; set; }
        [FlatBufferItem(2)] public TrackFlagsInfo Info { get; set; } = new TrackFlagsInfo();
    }

    // -------------------------
    // Material (kept for parsing completeness; not used by the viewer yet)
    // -------------------------

    [FlatBufferTable]
    public class TrackMaterialValue
    {
        [FlatBufferItem(0)] public float Time { get; set; }
        [FlatBufferItem(1)] public float Value { get; set; }
        [FlatBufferItem(2)] public uint Config0 { get; set; }
        [FlatBufferItem(3)] public uint Config1 { get; set; }
        [FlatBufferItem(4)] public uint Config2 { get; set; }
    }

    [FlatBufferTable]
    public class TrackMaterialValueList
    {
        [FlatBufferItem(0)] public IList<TrackMaterialValue> Values { get; set; } = new List<TrackMaterialValue>();
    }

    [FlatBufferTable]
    public class TrackMaterialInit
    {
        [FlatBufferItem(0)] public string Name { get; set; } = string.Empty;
        [FlatBufferItem(1)] public TrackMaterialValueList List { get; set; } = new TrackMaterialValueList();
    }

    [FlatBufferTable]
    public class TrackMaterialChannels
    {
        [FlatBufferItem(0)] public TrackMaterialValueList Red { get; set; } = new TrackMaterialValueList();
        [FlatBufferItem(1)] public TrackMaterialValueList Green { get; set; } = new TrackMaterialValueList();
        [FlatBufferItem(2)] public TrackMaterialValueList Blue { get; set; } = new TrackMaterialValueList();
        [FlatBufferItem(3)] public TrackMaterialValueList Alpha { get; set; } = new TrackMaterialValueList();
    }

    [FlatBufferTable]
    public class TrackMaterialAnim
    {
        [FlatBufferItem(0)] public string Name { get; set; } = string.Empty;
        [FlatBufferItem(1)] public TrackMaterialChannels List { get; set; } = new TrackMaterialChannels();
    }

    [FlatBufferTable]
    public class TrackMaterial
    {
        [FlatBufferItem(0)] public string Name { get; set; } = string.Empty;
        [FlatBufferItem(1)] public IList<TrackMaterialInit> InitValues { get; set; } = new List<TrackMaterialInit>();
        [FlatBufferItem(2)] public IList<TrackMaterialAnim> AnimValues { get; set; } = new List<TrackMaterialAnim>();
    }

    [FlatBufferTable]
    public class TrackMaterialTimeline
    {
        [FlatBufferItem(0)] public TrackConfig Res0 { get; set; } = new TrackConfig();
        [FlatBufferItem(1)] public uint Res1 { get; set; }
        [FlatBufferItem(2)] public IList<TrackMaterial> MaterialTrack { get; set; } = new List<TrackMaterial>();
        [FlatBufferItem(3)] public byte Unk3 { get; set; }
        [FlatBufferItem(4)] public byte Unk4 { get; set; }
    }

    // -------------------------
    // Blend shapes
    // -------------------------

    [FlatBufferTable]
    public class TrackBlendshapeInit
    {
        [FlatBufferItem(0)] public string MeshName { get; set; } = string.Empty;
        [FlatBufferItem(1)] public IList<string> BlendshapeList { get; set; } = new List<string>();
    }

    [FlatBufferTable]
    public class FixedBlendTrack
    {
        [FlatBufferItem(0)] public float Value { get; set; }
    }

    [FlatBufferTable]
    public class DynamicBlendTrack
    {
        [FlatBufferItem(0)] public IList<float> Value { get; set; } = new List<float>();
    }

    [FlatBufferTable]
    public class Framed16BlendTrack
    {
        [FlatBufferItem(0)] public IList<ushort> Frames { get; set; } = new List<ushort>();
        [FlatBufferItem(1)] public IList<float> Value { get; set; } = new List<float>();
    }

    [FlatBufferTable]
    public class Framed8BlendTrack
    {
        [FlatBufferItem(0)] public IList<byte> Frames { get; set; } = new List<byte>();
        [FlatBufferItem(1)] public IList<float> Value { get; set; } = new List<float>();
    }

    [FlatBufferTable]
    public class TrackBlendShape
    {
        [FlatBufferItem(0)] public string Name { get; set; } = string.Empty;
        [FlatBufferItem(1)] public IList<TrackBlendshapeInit> InitValues { get; set; } = new List<TrackBlendshapeInit>();
        [FlatBufferItem(2)]
        public FlatSharp.FlatBufferUnion<FixedBlendTrack, DynamicBlendTrack, Framed16BlendTrack, Framed8BlendTrack> Track { get; set; }
        [FlatBufferItem(4)] public uint Unk4 { get; set; }
    }

    [FlatBufferTable]
    public class BlendTable
    {
        [FlatBufferItem(0)] public string Name { get; set; } = string.Empty;
    }

    [FlatBufferTable]
    public class BlendShapeTimeline
    {
        [FlatBufferItem(0)] public TrackConfig Config { get; set; } = new TrackConfig();
        [FlatBufferItem(1)] public uint Res1 { get; set; }
        [FlatBufferItem(2)] public IList<TrackBlendShape> Res2 { get; set; } = new List<TrackBlendShape>();
        [FlatBufferItem(3)] public uint Res3 { get; set; }
        [FlatBufferItem(4)] public IList<BlendTable> BlendList { get; set; } = new List<BlendTable>();
    }
}
