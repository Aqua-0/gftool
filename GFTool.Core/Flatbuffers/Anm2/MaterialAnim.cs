using System.Collections.Generic;
using FlatSharp.Attributes;

namespace Trinity.Core.Flatbuffers.Anm2
{
    // Minimal ANM2 material animation bindings used by TSV.

    [FlatBufferEnum(typeof(uint))]
    public enum CurveInterpType : uint
    {
        Constant = 0,
        Linear = 1,
        TangentAuto = 2,
        TangentBreak = 3,
        TangentUser = 4,
    }

    [FlatBufferTable]
    public class RuntimeCurveKey
    {
        [FlatBufferItem(0)] public float Time { get; set; }
        [FlatBufferItem(1)] public float Value { get; set; }
        [FlatBufferItem(2)] public float TangentLeft { get; set; }
        [FlatBufferItem(3)] public float TangentRight { get; set; }
        [FlatBufferItem(4)] public CurveInterpType InterpType { get; set; }
    }

    [FlatBufferTable]
    public class RuntimeCurveFloat
    {
        [FlatBufferItem(0)] public IList<RuntimeCurveKey> Keys { get; set; } = new List<RuntimeCurveKey>();
    }

    [FlatBufferTable]
    public class RuntimeCurveFloat4
    {
        [FlatBufferItem(0)] public RuntimeCurveFloat X { get; set; } = new RuntimeCurveFloat();
        [FlatBufferItem(1)] public RuntimeCurveFloat Y { get; set; } = new RuntimeCurveFloat();
        [FlatBufferItem(2)] public RuntimeCurveFloat Z { get; set; } = new RuntimeCurveFloat();
        [FlatBufferItem(3)] public RuntimeCurveFloat W { get; set; } = new RuntimeCurveFloat();
    }

    [FlatBufferTable]
    public class MaterialParamFloat
    {
        [FlatBufferItem(0)] public string AttributeName { get; set; } = string.Empty;
        [FlatBufferItem(1)] public RuntimeCurveFloat CurveData { get; set; } = new RuntimeCurveFloat();
    }

    [FlatBufferTable]
    public class MaterialParamFloat4
    {
        [FlatBufferItem(0)] public string AttributeName { get; set; } = string.Empty;
        [FlatBufferItem(1)] public RuntimeCurveFloat4 CurveData { get; set; } = new RuntimeCurveFloat4();
    }

    [FlatBufferTable]
    public class MaterialParam
    {
        [FlatBufferItem(0)] public string MaterialName { get; set; } = string.Empty;
        [FlatBufferItem(1)] public IList<MaterialParamFloat> FloatParams { get; set; } = new List<MaterialParamFloat>();
        [FlatBufferItem(2)] public IList<MaterialParamFloat4> Float4Params { get; set; } = new List<MaterialParamFloat4>();
        [FlatBufferItem(3)] public IList<MaterialParamFloat> IntParams { get; set; } = new List<MaterialParamFloat>();
    }

    [FlatBufferTable]
    public class MaterialAnimationData
    {
        // FrameSetting/editorMaterialParams are omitted; observed runtime blobs often only populate runtime params.
        [FlatBufferItem(2)] public IList<MaterialParam> MaterialParams { get; set; } = new List<MaterialParam>();
        [FlatBufferItem(3)] public byte TotalFloatParamCount { get; set; }
        [FlatBufferItem(4)] public byte TotalFloat4ParamCount { get; set; }
        [FlatBufferItem(5)] public byte TotalIntParamCount { get; set; }
    }
}
