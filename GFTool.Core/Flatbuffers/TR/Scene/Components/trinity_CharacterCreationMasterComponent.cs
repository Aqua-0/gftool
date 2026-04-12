using FlatSharp.Attributes;

namespace GFTool.Core.Flatbuffers.TR.Scene.Components
{
    [FlatBufferTable]
    public class CcdataMasterEntry
    {
        [FlatBufferItem(0)]
        public string Part { get; set; }

        [FlatBufferItem(1)]
        public string File { get; set; }

        [FlatBufferItem(2)]
        public string Name { get; set; }
    }

    [FlatBufferTable]
    public class trinity_CharacterCreationMasterComponent
    {
        [FlatBufferItem(0)]
        public string Name { get; set; }

        [FlatBufferItem(1)]
        public uint res_1 { get; set; }

        [FlatBufferItem(2)]
        public float unk_2 { get; set; }

        [FlatBufferItem(3)]
        public float unk_3 { get; set; }

        [FlatBufferItem(4)]
        public float unk_4 { get; set; }

        [FlatBufferItem(5)]
        public uint res_5 { get; set; }

        [FlatBufferItem(6)]
        public float unk_6 { get; set; }

        [FlatBufferItem(7)]
        public uint unk_7 { get; set; }

        [FlatBufferItem(8)]
        public float unk_8 { get; set; }

        [FlatBufferItem(9)]
        public uint unk_9 { get; set; }

        [FlatBufferItem(10)]
        public float unk_10 { get; set; }

        [FlatBufferItem(11)]
        public uint unk_11 { get; set; }

        [FlatBufferItem(12)]
        public float unk_12 { get; set; }

        [FlatBufferItem(13)]
        public uint res_13 { get; set; }

        [FlatBufferItem(14)]
        public uint res_14 { get; set; }

        [FlatBufferItem(15)]
        public CcdataMasterEntry[] ccdataMasterList { get; set; }

        [FlatBufferItem(16)]
        public uint[] unk_16 { get; set; }
    }
}
