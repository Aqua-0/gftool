using FlatSharp.Attributes;

namespace GFTool.Core.Flatbuffers.TR.Scene.Components
{
    [FlatBufferTable]
    public class SubScene
    {
        // Trinity names this field "name"; older viewer code used Filepath, so keep that API stable.
        [FlatBufferItem(0)]
        public string Filepath { get; set; }

        [FlatBufferItem(1)]
        public string Group { get; set; }

        [FlatBufferItem(2)]
        public string Padding1 { get; set; }

        [FlatBufferItem(3)]
        public string Padding2 { get; set; }

        [FlatBufferItem(4)]
        public byte[] Data { get; set; }
    }
}
