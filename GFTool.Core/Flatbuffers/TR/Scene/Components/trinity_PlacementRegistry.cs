using FlatSharp;
using FlatSharp.Attributes;
using System.Collections.Generic;

namespace GFTool.Core.Flatbuffers.TR.Scene.Components
{
    [FlatBufferStruct]
    public class PackedVec3f
    {
        [FlatBufferItem(0)] public float X { get; set; }
        [FlatBufferItem(1)] public float Y { get; set; }
        [FlatBufferItem(2)] public float Z { get; set; }
    }

    [FlatBufferTable]
    public class trinity_PlacementRegistry
    {
        [FlatBufferItem(0)]
        public FlatBufferUnion<
            PlacementObjectArray,
            PlacementObjectTemplateArray,
            PlacementPositionArray,
            PlacementSpawnerArray> Entry { get; set; }
    }

    [FlatBufferTable]
    public class PlacementLogic
    {
        [FlatBufferItem(0)] public string Name { get; set; }
        [FlatBufferItem(1)] public LogicExpression Expression { get; set; }
    }

    [FlatBufferTable]
    public class PlacementSpawner
    {
        [FlatBufferItem(0)] public string Name { get; set; }
        [FlatBufferItem(1)] public string Scene { get; set; }
        [FlatBufferItem(2)] public IList<PlacementLogic> Arguments { get; set; }
    }

    [FlatBufferTable]
    public class PlacementPosition
    {
        [FlatBufferItem(0)] public string Name { get; set; }
        [FlatBufferItem(1)] public PackedVec3f Position { get; set; }
        [FlatBufferItem(2)] public PackedVec3f Rotation { get; set; }
        [FlatBufferItem(3)] public IList<string> Arguments { get; set; }
    }

    [FlatBufferTable]
    public class PlacementObjectTemplate
    {
        [FlatBufferItem(0)] public string Name { get; set; }
        [FlatBufferItem(1)] public string Path { get; set; }
    }

    [FlatBufferTable]
    public class PlacementObject
    {
        [FlatBufferItem(0)] public string Name { get; set; }
        [FlatBufferItem(1)] public string Type { get; set; }
        [FlatBufferItem(2)] public string File { get; set; }
    }

    [FlatBufferTable]
    public class PlacementSpawnerArray
    {
        [FlatBufferItem(0)] public IList<PlacementSpawner> Table { get; set; }
    }

    [FlatBufferTable]
    public class PlacementPositionArray
    {
        [FlatBufferItem(0)] public IList<PlacementPosition> Table { get; set; }
    }

    [FlatBufferTable]
    public class PlacementObjectTemplateArray
    {
        [FlatBufferItem(0)] public IList<PlacementObjectTemplate> Table { get; set; }
    }

    [FlatBufferTable]
    public class PlacementObjectArray
    {
        [FlatBufferItem(0)] public IList<PlacementObject> Table { get; set; }
    }

    [FlatBufferTable]
    public class ExpressionLeaf
    {
        [FlatBufferItem(0)] public string ConditionName { get; set; }
        [FlatBufferItem(1)] public int Op { get; set; }
        [FlatBufferItem(2)] public IList<string> Arguments { get; set; }
    }

    [FlatBufferEnum(typeof(int))]
    public enum ExpressionBranchOperand
    {
        And = 0,
        Or = 1,
    }

    [FlatBufferTable]
    public class ExpressionBranch
    {
        [FlatBufferItem(0)] public ExpressionBranchOperand Operand { get; set; }
        [FlatBufferItem(1)] public FlatBufferUnion<ExpressionLeaf, ExpressionBranch> LeftCondition { get; set; }
        [FlatBufferItem(3)] public FlatBufferUnion<ExpressionLeaf, ExpressionBranch> Left { get; set; }
        [FlatBufferItem(5)] public FlatBufferUnion<ExpressionLeaf, ExpressionBranch> RightCondition { get; set; }
        [FlatBufferItem(7)] public FlatBufferUnion<ExpressionLeaf, ExpressionBranch> Right { get; set; }
    }

    [FlatBufferTable]
    public class LogicExpression
    {
        [FlatBufferItem(0)] public FlatBufferUnion<ExpressionLeaf, ExpressionBranch> Root { get; set; }
    }
}
