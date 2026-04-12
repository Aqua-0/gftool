using System;
using FlatSharp.Attributes;

namespace Trinity.Core.Flatbuffers.Gfx2
{
    // Minimal TRMSH morph metadata bindings.

    [FlatBufferStruct]
    public class Gfx2Vector3f
    {
        [FlatBufferItem(0)] public float X { get; set; }
        [FlatBufferItem(1)] public float Y { get; set; }
        [FlatBufferItem(2)] public float Z { get; set; }
    }

    [FlatBufferStruct]
    public class Gfx2Vector4f
    {
        [FlatBufferItem(0)] public float X { get; set; }
        [FlatBufferItem(1)] public float Y { get; set; }
        [FlatBufferItem(2)] public float Z { get; set; }
        [FlatBufferItem(3)] public float W { get; set; }
    }

    [FlatBufferTable]
    public class Gfx2Aabb
    {
        [FlatBufferItem(0)] public Gfx2Vector3f Min { get; set; }
        [FlatBufferItem(1)] public Gfx2Vector3f Max { get; set; }
    }

    [FlatBufferEnum(typeof(uint))]
    public enum Gfx2IndexBufferType : uint
    {
        Unknown = 0,
        Short = 1,
        Int = 2,
        Count = 3
    }

    [FlatBufferTable]
    public class Gfx2SubMesh
    {
        [FlatBufferItem(0)] public int Count { get; set; }
        [FlatBufferItem(1)] public int IndexOffset { get; set; }
        [FlatBufferItem(2)] public int VertexOffset { get; set; }
        [FlatBufferItem(3)] public string MaterialName { get; set; } = string.Empty;
        [FlatBufferItem(4, DefaultValue = -1)] public int IndexBufferId { get; set; } = -1;
    }

    [FlatBufferTable]
    public class Gfx2JointTotalWeight
    {
        [FlatBufferItem(0)] public uint Id { get; set; }
        [FlatBufferItem(1)] public float Weight { get; set; }
    }

    [FlatBufferEnum(typeof(uint))]
    public enum Gfx2VertexAttributeSemantic : uint
    {
        Unknown = 0,
        Position = 1,
        Normal = 2,
        Tangent = 3,
        Binormal = 4,
        Color = 5,
        TexCoord = 6,
        JointIndices = 7,
        JointWeight = 8,
        VertexIndex = 9,
        TargetVertexIndex = 10,
        Count = 11
    }

    [FlatBufferEnum(typeof(uint))]
    public enum Gfx2VertexAttributeFormat : uint
    {
        Unknown = 0,
        R8G8B8A8_UNORM = 20,
        R8G8B8A8_UINT = 22,
        R32_UINT = 36,
        R32_SINT = 37,
        R16G16B16A16_UNORM = 39,
        R16G16B16A16_FLOAT = 43,
        R32G32_FLOAT = 48,
        R32G32B32_FLOAT = 51,
        R32G32B32A32_FLOAT = 54,
        Count = 55
    }

    [FlatBufferTable]
    public class Gfx2VertexAttribute
    {
        [FlatBufferItem(0, DefaultValue = -1)]
        public int StreamId { get; set; } = -1;

        [FlatBufferItem(1)]
        public Gfx2VertexAttributeSemantic Semantic { get; set; }

        [FlatBufferItem(2)]
        public int SemanticId { get; set; }

        [FlatBufferItem(3)]
        public Gfx2VertexAttributeFormat Format { get; set; }

        [FlatBufferItem(4)]
        public int Offset { get; set; }
    }

    [FlatBufferTable]
    public class Gfx2VertexStream
    {
        [FlatBufferItem(0)]
        public int Stride { get; set; }
    }

    [FlatBufferTable]
    public class Gfx2VertexLayer
    {
        [FlatBufferItem(0)]
        public Gfx2VertexAttribute[] AttributeList { get; set; } = Array.Empty<Gfx2VertexAttribute>();

        [FlatBufferItem(1)]
        public Gfx2VertexStream[] StreamList { get; set; } = Array.Empty<Gfx2VertexStream>();
    }

    [FlatBufferTable]
    public class Gfx2BlendShapeInfo
    {
        [FlatBufferItem(0)]
        public uint VertexLayerId { get; set; }

        [FlatBufferItem(1)]
        public string Name { get; set; } = string.Empty;

        [FlatBufferItem(2)]
        public bool IsAdditiveBlend { get; set; }
    }

    [FlatBufferTable]
    public class Gfx2PartialBlendShapeInfo
    {
        [FlatBufferItem(0)]
        public Gfx2VertexLayer[] VertexLayerList { get; set; } = Array.Empty<Gfx2VertexLayer>();

        [FlatBufferItem(1)]
        public Gfx2BlendShapeInfo[] BlendShapeInfoList { get; set; } = Array.Empty<Gfx2BlendShapeInfo>();

        [FlatBufferItem(2)]
        public string Name { get; set; } = string.Empty;
    }

    [FlatBufferTable]
    public class Gfx2MeshItem
    {
        [FlatBufferItem(0)]
        public string Name { get; set; } = string.Empty;

        // Keep items contiguous to match the schema exactly; some FlatBuffers readers (and some FlatSharp modes)
        // are sensitive to sparse schemas.
        [FlatBufferItem(1)]
        public Gfx2Aabb? Aabb { get; set; }

        [FlatBufferItem(2)]
        public Gfx2IndexBufferType IndexBufferType { get; set; }

        [FlatBufferItem(3)]
        public Gfx2VertexLayer[] VertexLayerList { get; set; } = Array.Empty<Gfx2VertexLayer>();

        [FlatBufferItem(4)]
        public Gfx2SubMesh[] SubMeshList { get; set; } = Array.Empty<Gfx2SubMesh>();

        [FlatBufferItem(5)]
        public int DrawLayer { get; set; }

        [FlatBufferItem(6)]
        public int SortingPriority { get; set; }

        [FlatBufferItem(7)]
        public bool CastShadow { get; set; } = true;

        [FlatBufferItem(8)]
        public bool ReceiveShadow { get; set; } = true;

        [FlatBufferItem(9)]
        public Gfx2Vector4f BoundingSphere { get; set; }

        [FlatBufferItem(10)]
        public Gfx2JointTotalWeight[] JointTotalWeightList { get; set; } = Array.Empty<Gfx2JointTotalWeight>();

        [FlatBufferItem(11)]
        public Gfx2BlendShapeInfo[] BlendShapeInfoList { get; set; } = Array.Empty<Gfx2BlendShapeInfo>();

        [FlatBufferItem(12)]
        public string ParentNodeName { get; set; } = string.Empty;

        [FlatBufferItem(13)]
        public int StateVisibleForCapture { get; set; }

        [FlatBufferItem(14)]
        public Gfx2PartialBlendShapeInfo[] PartialBlendShapeInfoList { get; set; } = Array.Empty<Gfx2PartialBlendShapeInfo>();
    }

    [FlatBufferTable]
    public class Gfx2Mesh
    {
        [FlatBufferItem(0)]
        public uint Version { get; set; }

        [FlatBufferItem(1)]
        public Gfx2MeshItem[] ItemList { get; set; } = Array.Empty<Gfx2MeshItem>();

        [FlatBufferItem(2)]
        public string MeshBufferPath { get; set; } = string.Empty;
    }
}
