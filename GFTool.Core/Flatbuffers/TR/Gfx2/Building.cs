using FlatSharp.Attributes;
using Trinity.Core.Flatbuffers.Utils;

namespace GFTool.Core.Flatbuffers.TR.Gfx2
{
    [FlatBufferTable]
    public class TrBuilding
    {
        [FlatBufferItem(0)]
        public TrBuildingChipInfo[] Chips { get; set; } = [];

        [FlatBufferItem(1)]
        public string UniqueModelName { get; set; } = string.Empty;
    }

    [FlatBufferTable]
    public class TrBuildingChipInfo
    {
        [FlatBufferItem(0)]
        public string ModelName { get; set; } = string.Empty;

        [FlatBufferItem(1)]
        public TrBuildingSrt Srt { get; set; } = new();

        [FlatBufferItem(2)]
        public uint PaletteIndex { get; set; }
    }

    [FlatBufferTable]
    public class TrBuildingSrt
    {
        [FlatBufferItem(0)]
        public Vector3f Scale { get; set; } = new() { X = 1.0f, Y = 1.0f, Z = 1.0f };

        [FlatBufferItem(1)]
        public Vector3f Rotate { get; set; } = new();

        [FlatBufferItem(2)]
        public Vector3f Translate { get; set; } = new();
    }
}
