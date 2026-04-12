using FlatSharp.Attributes;
using Trinity.Core.Flatbuffers.Utils;

namespace Trinity.Core.Flatbuffers.TR.Model
{
    // TRMTR schema used by SV/ZA.
    // This file models the on-disk TRMTR structure. The renderer still consumes `TRMaterial`
    // as its runtime representation.

    [FlatBufferTable]
    public class TrmtrFileStringParameter
    {
        [FlatBufferItem(0)] public string Name { get; set; } = string.Empty;
        [FlatBufferItem(1)] public string Value { get; set; } = string.Empty;
    }

    [FlatBufferTable]
    public class TrmtrFileShader
    {
        [FlatBufferItem(0)] public string Name { get; set; } = string.Empty;
        [FlatBufferItem(1)] public TrmtrFileStringParameter[] Values { get; set; } = Array.Empty<TrmtrFileStringParameter>();
    }

    [FlatBufferTable]
    public class TrmtrFileTexture
    {
        [FlatBufferItem(0)] public string Name { get; set; } = string.Empty;
        [FlatBufferItem(1)] public string File { get; set; } = string.Empty;
        [FlatBufferItem(2)] public uint Slot { get; set; } = 0;
    }

    [FlatBufferTable]
    public class TrmtrFileSamplerState
    {
        [FlatBufferItem(0)] public uint State0 { get; set; } = 0;
        [FlatBufferItem(1)] public uint State1 { get; set; } = 0;
        [FlatBufferItem(2)] public uint State2 { get; set; } = 0;
        [FlatBufferItem(3)] public uint State3 { get; set; } = 0;
        [FlatBufferItem(4)] public uint State4 { get; set; } = 0;
        [FlatBufferItem(5)] public uint State5 { get; set; } = 0;
        [FlatBufferItem(6)] public uint State6 { get; set; } = 0;
        [FlatBufferItem(7)] public uint State7 { get; set; } = 0;
        [FlatBufferItem(8)] public uint State8 { get; set; } = 0;
        [FlatBufferItem(9)] public UVWrapMode RepeatU { get; set; } = UVWrapMode.WRAP;
        [FlatBufferItem(10)] public UVWrapMode RepeatV { get; set; } = UVWrapMode.WRAP;
        [FlatBufferItem(11)] public UVWrapMode RepeatW { get; set; } = UVWrapMode.WRAP;
        [FlatBufferItem(12)] public RGBA BorderColor { get; set; } = new RGBA();
    }

    [FlatBufferTable]
    public class TrmtrFileVector2fParameter
    {
        [FlatBufferItem(0)] public string Name { get; set; } = string.Empty;
        [FlatBufferItem(1)] public Vector2f Value { get; set; } = new Vector2f();
    }

    [FlatBufferTable]
    public class TrmtrFileVector3fParameter
    {
        [FlatBufferItem(0)] public string Name { get; set; } = string.Empty;
        [FlatBufferItem(1)] public Vector3f Value { get; set; } = new Vector3f();
    }

    [FlatBufferTable]
    public class TrmtrFileFloatParameter
    {
        [FlatBufferItem(0)] public string Name { get; set; } = string.Empty;
        [FlatBufferItem(1)] public float Value { get; set; }
    }

    [FlatBufferTable]
    public class TrmtrFileFloat4Parameter
    {
        [FlatBufferItem(0)] public string Name { get; set; } = string.Empty;
        [FlatBufferItem(1)] public RGBA Value { get; set; } = new RGBA();
    }

    [FlatBufferTable]
    public class TrmtrFileMatrix4x4f
    {
        [FlatBufferItem(0)] public Vector4f Row0 { get; set; } = new Vector4f();
        [FlatBufferItem(1)] public Vector4f Row1 { get; set; } = new Vector4f();
        [FlatBufferItem(2)] public Vector4f Row2 { get; set; } = new Vector4f();
        [FlatBufferItem(3)] public Vector4f Row3 { get; set; } = new Vector4f();
    }

    [FlatBufferTable]
    public class TrmtrFileMatrix4x4fParameter
    {
        [FlatBufferItem(0)] public string Name { get; set; } = string.Empty;
        [FlatBufferItem(1)] public TrmtrFileMatrix4x4f Value { get; set; } = new TrmtrFileMatrix4x4f();
    }

    [FlatBufferTable]
    public class TrmtrFileIntParameter
    {
        [FlatBufferItem(0)] public string Name { get; set; } = string.Empty;
        [FlatBufferItem(1, DefaultValue = -1)] public int Value { get; set; } = -1;
    }

    [FlatBufferTable]
    public class TrmtrFileVector2i
    {
        [FlatBufferItem(0)] public int X { get; set; }
        [FlatBufferItem(1)] public int Y { get; set; }
    }

    [FlatBufferTable]
    public class TrmtrFileVector3i
    {
        [FlatBufferItem(0)] public int X { get; set; }
        [FlatBufferItem(1)] public int Y { get; set; }
        [FlatBufferItem(2)] public int Z { get; set; }
    }

    [FlatBufferTable]
    public class TrmtrFileVector4i
    {
        [FlatBufferItem(0)] public int X { get; set; }
        [FlatBufferItem(1)] public int Y { get; set; }
        [FlatBufferItem(2)] public int Z { get; set; }
        [FlatBufferItem(3)] public int W { get; set; }
    }

    [FlatBufferTable]
    public class TrmtrFileVector2iParameter
    {
        [FlatBufferItem(0)] public string Name { get; set; } = string.Empty;
        [FlatBufferItem(1)] public TrmtrFileVector2i Value { get; set; } = new TrmtrFileVector2i();
    }

    [FlatBufferTable]
    public class TrmtrFileVector3iParameter
    {
        [FlatBufferItem(0)] public string Name { get; set; } = string.Empty;
        [FlatBufferItem(1)] public TrmtrFileVector3i Value { get; set; } = new TrmtrFileVector3i();
    }

    [FlatBufferTable]
    public class TrmtrFileVector4iParameter
    {
        [FlatBufferItem(0)] public string Name { get; set; } = string.Empty;
        [FlatBufferItem(1)] public TrmtrFileVector4i Value { get; set; } = new TrmtrFileVector4i();
    }

    [FlatBufferEnum(typeof(uint))]
    public enum TrmtrFrontFace : uint
    {
        CW = 0,
        CCW = 1
    }

    [FlatBufferEnum(typeof(uint))]
    public enum TrmtrCullMode : uint
    {
        None = 0,
        Front = 1,
        Back = 2,
        FrontAndBack = 3
    }

    [FlatBufferTable]
    public class TrmtrFileRasterizationState
    {
        [FlatBufferItem(0)] public TrmtrFrontFace FrontFace { get; set; } = TrmtrFrontFace.CW;
        [FlatBufferItem(1)] public TrmtrCullMode CullMode { get; set; } = TrmtrCullMode.Back;
        [FlatBufferItem(2)] public int DepthBias { get; set; } = 0;
        [FlatBufferItem(3)] public float SlopeScaledDepthBias { get; set; } = 0.0f;
        [FlatBufferItem(4)] public float DepthBiasClamp { get; set; } = 0.0f;
    }

    [FlatBufferTable]
    public class TrmtrFileDepthState
    {
        [FlatBufferItem(0)] public bool DepthWriteEnable { get; set; } = true;
        [FlatBufferItem(1)] public bool DepthTestEnable { get; set; } = true;
    }

    [FlatBufferTable]
    public class TrmtrFileMaterial
    {
        [FlatBufferItem(0)] public string Name { get; set; } = string.Empty;
        [FlatBufferItem(1)] public TrmtrFileShader[] Shaders { get; set; } = Array.Empty<TrmtrFileShader>();
        [FlatBufferItem(2)] public TrmtrFileTexture[] Textures { get; set; } = Array.Empty<TrmtrFileTexture>();
        [FlatBufferItem(3)] public TrmtrFileSamplerState[] Samplers { get; set; } = Array.Empty<TrmtrFileSamplerState>();
        [FlatBufferItem(4)] public TrmtrFileFloatParameter[] FloatParameters { get; set; } = Array.Empty<TrmtrFileFloatParameter>();
        [FlatBufferItem(5)] public TrmtrFileVector2fParameter[] Vector2fParameters { get; set; } = Array.Empty<TrmtrFileVector2fParameter>();
        [FlatBufferItem(6)] public TrmtrFileVector3fParameter[] Vector3fParameters { get; set; } = Array.Empty<TrmtrFileVector3fParameter>();
        [FlatBufferItem(7)] public TrmtrFileFloat4Parameter[] Float4Parameters { get; set; } = Array.Empty<TrmtrFileFloat4Parameter>();
        [FlatBufferItem(8)] public TrmtrFileMatrix4x4fParameter[] Matrix4x4fParameters { get; set; } = Array.Empty<TrmtrFileMatrix4x4fParameter>();
        [FlatBufferItem(9)] public TrmtrFileIntParameter[] IntParameters { get; set; } = Array.Empty<TrmtrFileIntParameter>();
        [FlatBufferItem(10)] public TrmtrFileVector2iParameter[] Vector2iParameters { get; set; } = Array.Empty<TrmtrFileVector2iParameter>();
        [FlatBufferItem(11)] public TrmtrFileVector3iParameter[] Vector3iParameters { get; set; } = Array.Empty<TrmtrFileVector3iParameter>();
        [FlatBufferItem(12)] public TrmtrFileVector4iParameter[] Vector4iParameters { get; set; } = Array.Empty<TrmtrFileVector4iParameter>();
        [FlatBufferItem(13)] public TrmtrFileDepthState? DepthState { get; set; }
        [FlatBufferItem(14)] public TrmtrFileRasterizationState? RasterizationState { get; set; }
        [FlatBufferItem(15)] public string BlendStatePreset { get; set; } = string.Empty;
        [FlatBufferItem(16)] public bool CastShadow { get; set; } = true;
        [FlatBufferItem(17)] public bool ReceiveShadow { get; set; } = true;
    }

    [FlatBufferTable]
    public class TrmtrFile
    {
        [FlatBufferItem(0)] public uint Field0 { get; set; }
        [FlatBufferItem(1)] public TrmtrFileMaterial[] Materials { get; set; } = Array.Empty<TrmtrFileMaterial>();
    }
}
