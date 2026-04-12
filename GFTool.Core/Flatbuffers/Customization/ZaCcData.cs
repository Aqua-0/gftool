using FlatSharp.Attributes;

namespace GFTool.Core.Flatbuffers.Customization
{
    // Minimal ZA character creation bindings used by TSV.
    [FlatBufferTable]
    public class ZaCcPartsData
    {
        [FlatBufferItem(0)]
        public string Name { get; set; }

        [FlatBufferItem(1)]
        public bool RootPart { get; set; }

        [FlatBufferItem(2)]
        public string TrmdlFilePath { get; set; }

        [FlatBufferItem(3)]
        public string TrmmtFilePath { get; set; }

        [FlatBufferItem(4)]
        public string TracnFilePath { get; set; }

        [FlatBufferItem(5)]
        public string MetadataName { get; set; }

        [FlatBufferItem(6)]
        public string[] TracnFilePathList { get; set; }

        [FlatBufferItem(7)]
        public bool UsesAnimationLayer { get; set; }

        [FlatBufferItem(8)]
        public string[] SharedPartsAnimationFileList { get; set; }

        [FlatBufferItem(9)]
        public string[] SharedPartsLayerList { get; set; }
    }

    [FlatBufferTable]
    public class ZaCcData
    {
        [FlatBufferItem(0)]
        public ZaCcPartsData[] PartsList { get; set; }
    }
}
