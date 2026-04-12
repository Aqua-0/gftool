using FlatSharp.Attributes;

namespace GFTool.Core.Flatbuffers.Customization
{
    // Minimal ZA character creation index bindings used by TSV.
    [FlatBufferTable]
    public class ZaCcDatamUnit
    {
        [FlatBufferItem(0)]
        public string Label { get; set; }

        [FlatBufferItem(1)]
        public string CcdataPath { get; set; }

        [FlatBufferItem(2)]
        public string Variation { get; set; }
    }

    [FlatBufferTable]
    public class ZaCcDatam
    {
        [FlatBufferItem(0)]
        public ZaCcDatamUnit[] CcdataList { get; set; }
    }
}
