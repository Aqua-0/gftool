using FlatSharp.Attributes;

namespace Titan.Resource
{
    // Minimal resource catalog bindings.
    [FlatBufferTable]
    public class VersionInfo
    {
        [FlatBufferItem(0)]
        public uint Version { get; set; }
    }

    [FlatBufferTable]
    public class SpeciesInfo
    {
        [FlatBufferItem(0)]
        public ushort Species { get; set; }

        [FlatBufferItem(1)]
        public ushort Form { get; set; }

        [FlatBufferItem(2)]
        public byte Gender { get; set; }
    }

    [FlatBufferTable]
    public class AnimationInfo
    {
        [FlatBufferItem(0)]
        public short FormNumber { get; set; }

        [FlatBufferItem(1)]
        public string Path { get; set; } = string.Empty;
    }

    [FlatBufferTable]
    public class LocatorInfo
    {
        [FlatBufferItem(0)]
        public short FormNumber { get; set; }

        [FlatBufferItem(1)]
        public byte LocIndex { get; set; }

        [FlatBufferItem(2)]
        public string LocPath { get; set; } = string.Empty;
    }

    [FlatBufferTable]
    public class CatalogEntry
    {
        [FlatBufferItem(0)]
        public SpeciesInfo? SpeciesInfo { get; set; }

        [FlatBufferItem(1)]
        public string ModelPath { get; set; } = string.Empty;

        [FlatBufferItem(2)]
        public string MaterialTablePath { get; set; } = string.Empty;

        [FlatBufferItem(3)]
        public string ConfigPath { get; set; } = string.Empty;

        [FlatBufferItem(4)]
        public AnimationInfo[]? Animations { get; set; }

        [FlatBufferItem(5)]
        public LocatorInfo[]? Locators { get; set; }

        [FlatBufferItem(6)]
        public string IconPath { get; set; } = string.Empty;

        [FlatBufferItem(7)]
        public uint UnkId { get; set; }

        [FlatBufferItem(8)]
        public string DefencePath { get; set; } = string.Empty;
    }

    [FlatBufferTable]
    public class Catalog
    {
        [FlatBufferItem(0)]
        public VersionInfo? Version { get; set; }

        [FlatBufferItem(1)]
        public CatalogEntry[]? Table { get; set; }
    }
}
