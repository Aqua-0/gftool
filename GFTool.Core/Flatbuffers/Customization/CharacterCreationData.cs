using FlatSharp.Attributes;

namespace GFTool.Core.Flatbuffers.Customization
{
    [FlatBufferTable]
    public class CharacterCreationSomeTable2
    {
        [FlatBufferItem(0)]
        public string Unk0 { get; set; }

        [FlatBufferItem(1)]
        public string Unk1 { get; set; }

        [FlatBufferItem(2)]
        public string Unk2 { get; set; }
    }

    [FlatBufferTable]
    public class CharacterCreationEntry
    {
        [FlatBufferItem(0)]
        public string Name { get; set; }

        [FlatBufferItem(1)]
        public byte Unk0 { get; set; }

        [FlatBufferItem(2)]
        public string ModelFile { get; set; }

        [FlatBufferItem(3)]
        public string MaterialFile { get; set; }

        [FlatBufferItem(4)]
        public byte Unk2 { get; set; }

        [FlatBufferItem(5)]
        public string Unk3 { get; set; }

        [FlatBufferItem(6)]
        public string[] AnimFiles { get; set; }
    }

    [FlatBufferTable]
    public class CharacterCreationEntry4
    {
        [FlatBufferItem(0)]
        public string Type { get; set; }

        [FlatBufferItem(1)]
        public uint Enable { get; set; }
    }

    [FlatBufferTable]
    public class CharacterCreationEntry3
    {
        [FlatBufferItem(0)]
        public string Name { get; set; }

        [FlatBufferItem(1)]
        public string Type { get; set; }

        [FlatBufferItem(2)]
        public CharacterCreationEntry4[] Unk0 { get; set; }
    }

    [FlatBufferTable]
    public class CharacterCreationEntry2
    {
        [FlatBufferItem(0)]
        public string Name { get; set; }

        [FlatBufferItem(1)]
        public CharacterCreationEntry3[] Entries { get; set; }
    }

    [FlatBufferTable]
    public class CharacterCreationData
    {
        [FlatBufferItem(0)]
        public CharacterCreationEntry[] Entries { get; set; }

        [FlatBufferItem(1)]
        public CharacterCreationSomeTable2 Unk1 { get; set; }

        [FlatBufferItem(2)]
        public uint Unk2 { get; set; }

        [FlatBufferItem(3)]
        public CharacterCreationEntry2[] Unk3 { get; set; }
    }
}
