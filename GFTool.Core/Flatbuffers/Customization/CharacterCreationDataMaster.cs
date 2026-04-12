using FlatSharp.Attributes;

namespace GFTool.Core.Flatbuffers.Customization
{
    [FlatBufferTable]
    public class CharacterCreationDataMasterEntry
    {
        [FlatBufferItem(0)]
        public string Name { get; set; }

        [FlatBufferItem(1)]
        public string CcdataFile { get; set; }

        [FlatBufferItem(2)]
        public string Unk0 { get; set; }
    }

    [FlatBufferTable]
    public class CharacterCreationDataMaster
    {
        [FlatBufferItem(0)]
        public CharacterCreationDataMasterEntry[] Entries { get; set; }
    }
}
