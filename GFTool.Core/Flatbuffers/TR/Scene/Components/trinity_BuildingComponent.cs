using FlatSharp.Attributes;

namespace GFTool.Core.Flatbuffers.TR.Scene.Components
{
    [FlatBufferTable]
    public class trinity_BuildingComponent
    {
        [FlatBufferItem(0)]
        public string FilePath { get; set; } = string.Empty;
    }
}
