using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace GFTool.Core.Particles.Ptcl
{
    public sealed class PtclFile
    {
        public sealed record TextureDescriptor(ulong TextureId, string Name);

        public sealed record SamplerInfo(ulong TextureId);

        public sealed class Emitter
        {
            public string Name { get; init; } = string.Empty;
            public Vector4 ConstantColor0 { get; init; }
            public Vector4 ConstantColor1 { get; init; }
            public IReadOnlyList<SamplerInfo> Samplers { get; init; } = Array.Empty<SamplerInfo>();
            public uint Color0KeyCount { get; init; }
            public uint Alpha0KeyCount { get; init; }
            public uint Color1KeyCount { get; init; }
            public uint Alpha1KeyCount { get; init; }

            public Vector4[] Color0Keys { get; init; } = Array.Empty<Vector4>();
            public Vector4[] Alpha0Keys { get; init; } = Array.Empty<Vector4>();
            public Vector4[] Color1Keys { get; init; } = Array.Empty<Vector4>();
            public Vector4[] Alpha1Keys { get; init; } = Array.Empty<Vector4>();
        }

        public byte[]? EmbeddedBntxBytes { get; private set; }
        public byte[]? EmbeddedBfresBytes { get; private set; }
        public IReadOnlyList<TextureDescriptor> Textures { get; private set; } = Array.Empty<TextureDescriptor>();
        public IReadOnlyList<Emitter> Emitters { get; private set; } = Array.Empty<Emitter>();
        public ushort VfxVersion { get; private set; }

        private PtclFile()
        {
        }

        public static bool TryLoadFromFile(string path, out PtclFile file, out string error)
        {
            file = null!;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Empty PTCL path.";
                return false;
            }

            try
            {
                var bytes = File.ReadAllBytes(path);
                return TryLoadFromBytes(bytes, out file, out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TryLoadFromBytes(byte[] bytes, out PtclFile file, out string error)
        {
            file = null!;
            error = string.Empty;

            if (bytes == null || bytes.Length < 64)
            {
                error = "PTCL data is empty or too small.";
                return false;
            }

            if (ReadU32(bytes, 0) != Sig("VFXB"))
            {
                error = "Not a VFXB PTCL.";
                return false;
            }

            ushort vfxVersion = ReadU16(bytes, 4 + 4 + 2); // padding u32 + gfx u16
            ushort blockOffset = ReadU16(bytes, 4 + 4 + 2 + 2 + 2 + 1 + 1 + 4 + 2); // header fields, see below

            // Header layout (little endian), mirroring Switch-Toolbox:
            // u32 padding
            // u16 GraphicsAPIVersion
            // u16 VFXVersion
            // u16 ByteOrderMark
            // u8 alignment
            // u8 targetOffset
            // u32 headerSize
            // u16 flag
            // u16 blockOffset
            // u32 padding2
            // u32 fileSize
            //
            // The blockOffset is the absolute offset of the first section.

            int rootSectionOffset = blockOffset;
            if (rootSectionOffset <= 0 || rootSectionOffset >= bytes.Length)
            {
                error = $"Invalid PTCL block offset: {rootSectionOffset}.";
                return false;
            }

            var emitters = new List<Emitter>();
            byte[]? bntxBytes = null;
            byte[]? bfresBytes = null;
            int? gtntOffset = null;

            foreach (var section in EnumerateSectionChain(bytes, rootSectionOffset))
            {
                if (section.Signature == "GRTF" && section.BinaryDataOffset != 0xFFFFFFFF)
                {
                    if (TrySlice(bytes, section.Position + (int)section.BinaryDataOffset, (int)section.SectionSize, out var slice))
                    {
                        bntxBytes = slice.ToArray();
                    }
                }
                else if (section.Signature == "GTNT" && section.BinaryDataOffset != 0xFFFFFFFF)
                {
                    gtntOffset = section.Position + (int)section.BinaryDataOffset;
                }
                else if (bfresBytes == null && section.Signature == "G3PR" && section.BinaryDataOffset != 0xFFFFFFFF)
                {
                    if (TryExtractEmbeddedBfres(bytes, section, out var extracted))
                    {
                        bfresBytes = extracted;
                    }
                }
                else if (section.Signature == "EMTR" && section.BinaryDataOffset != 0xFFFFFFFF)
                {
                    if (TryParseEmitter(bytes, section.Position, section.BinaryDataOffset, vfxVersion, out var emitter))
                    {
                        emitters.Add(emitter);
                    }
                }
            }

            var textures = new List<TextureDescriptor>();
            if (bntxBytes != null && gtntOffset.HasValue)
            {
                // Texture descriptors are keyed by TextureID and map to a BNTX texture name.
                // The GTNT table is typically small; parse up to a reasonable count, stopping when offsets go invalid.
                TryParseTextureDescriptors(bytes, gtntOffset.Value, 256, textures);
            }

            file = new PtclFile
            {
                EmbeddedBntxBytes = bntxBytes,
                EmbeddedBfresBytes = bfresBytes,
                Emitters = emitters,
                Textures = textures,
                VfxVersion = vfxVersion
            };
            return true;
        }

        private static bool TryExtractEmbeddedBfres(byte[] bytes, Section section, out byte[] bfresBytes)
        {
            bfresBytes = Array.Empty<byte>();

            if (section.BinaryDataOffset == 0xFFFFFFFF)
            {
                return false;
            }

            int payloadStart = section.Position + (int)section.BinaryDataOffset;
            int payloadLen = (int)section.SectionSize;
            if (payloadStart <= 0 || payloadLen <= 0 || payloadStart + payloadLen > bytes.Length)
            {
                return false;
            }

            var span = new ReadOnlySpan<byte>(bytes, payloadStart, payloadLen);
            int idx = IndexOf(span, Sig("FRES"));
            if (idx < 0)
            {
                return false;
            }

            int bfresStart = payloadStart + idx;
            if (bfresStart + 0x1C > bytes.Length)
            {
                return false;
            }

            // BFRES stores the file size as a u32 at 0x18 (little endian).
            uint size = ReadU32(bytes, bfresStart + 0x18);
            if (size < 0x40 || bfresStart + size > bytes.Length)
            {
                return false;
            }

            bfresBytes = new byte[size];
            Buffer.BlockCopy(bytes, bfresStart, bfresBytes, 0, (int)size);
            return true;
        }

        private static int IndexOf(ReadOnlySpan<byte> span, uint sig)
        {
            // Find 4-byte signature in span.
            if (span.Length < 4)
            {
                return -1;
            }

            for (int i = 0; i <= span.Length - 4; i++)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(i, 4)) == sig)
                {
                    return i;
                }
            }
            return -1;
        }

        private static bool TryParseEmitter(byte[] bytes, int sectionPos, uint binaryDataOffset, ushort vfxVersion, out Emitter emitter)
        {
            emitter = null!;

            int namePos = sectionPos + (int)binaryDataOffset + 16;
            if (!TryReadZString(bytes, namePos, 256, out var name))
            {
                return false;
            }

            int structPos = sectionPos + (int)binaryDataOffset + 16 + 64;
            if (structPos <= 0 || structPos >= bytes.Length)
            {
                return false;
            }

            // Switch-Toolbox's NX reader reads:
            // - ConstantColor0/1 at offset +2384
            // - Samplers (3) at offset +2464, each with a u64 TextureID
            int constantColorOffset =
                vfxVersion >= 37 ? 2392 :
                (vfxVersion > 21 ? 2384 : 2392);
            int samplerOffset =
                vfxVersion >= 37 ? 2472 :
                (vfxVersion > 21 ? 2464 : 2472);

            if (!TryReadVec4(bytes, structPos + constantColorOffset, out var c0) ||
                !TryReadVec4(bytes, structPos + constantColorOffset + 16, out var c1))
            {
                c0 = new Vector4(1, 1, 1, 1);
                c1 = new Vector4(1, 1, 1, 1);
            }

            uint color0KeyCount = 0;
            uint alpha0KeyCount = 0;
            uint color1KeyCount = 0;
            uint alpha1KeyCount = 0;
            int keyHeaderPos = structPos;
            if (keyHeaderPos + 16 + 5 * 4 <= bytes.Length)
            {
                // Matches Switch-Toolbox: 16 bytes padding, then key counts.
                color0KeyCount = ReadU32(bytes, keyHeaderPos + 16);
                alpha0KeyCount = ReadU32(bytes, keyHeaderPos + 20);
                color1KeyCount = ReadU32(bytes, keyHeaderPos + 24);
                alpha1KeyCount = ReadU32(bytes, keyHeaderPos + 28);
            }

            // Key tables at +880 (8 entries each), each entry = (r,g,b,time) floats.
            // We expose them as Vector4, using W=time.
            Vector4[] ReadKeyTable(int tablePos)
            {
                if (tablePos < 0 || tablePos + 8 * 16 > bytes.Length)
                {
                    return Array.Empty<Vector4>();
                }
                var keys = new Vector4[8];
                for (int i = 0; i < 8; i++)
                {
                    keys[i] = new Vector4(
                        ReadF32(bytes, tablePos + i * 16 + 0),
                        ReadF32(bytes, tablePos + i * 16 + 4),
                        ReadF32(bytes, tablePos + i * 16 + 8),
                        ReadF32(bytes, tablePos + i * 16 + 12));
                }
                return keys;
            }

            var color0Keys = ReadKeyTable(structPos + 880);
            var alpha0Keys = ReadKeyTable(structPos + 880 + 8 * 16);
            var color1Keys = ReadKeyTable(structPos + 880 + 16 * 16);
            var alpha1Keys = ReadKeyTable(structPos + 880 + 24 * 16);

            var samplers = new List<SamplerInfo>(capacity: 3);
            int p = structPos + samplerOffset;
            for (int i = 0; i < 3; i++)
            {
                if (p + 8 > bytes.Length) break;
                ulong texId = ReadU64(bytes, p);
                if (texId != 0)
                {
                    samplers.Add(new SamplerInfo(texId));
                }
                p += 32;
            }

            emitter = new Emitter
            {
                Name = name,
                ConstantColor0 = c0,
                ConstantColor1 = c1,
                Samplers = samplers,
                Color0KeyCount = color0KeyCount,
                Alpha0KeyCount = alpha0KeyCount,
                Color1KeyCount = color1KeyCount,
                Alpha1KeyCount = alpha1KeyCount,
                Color0Keys = color0Keys,
                Alpha0Keys = alpha0Keys,
                Color1Keys = color1Keys,
                Alpha1Keys = alpha1Keys
            };
            return true;
        }

        private static void TryParseTextureDescriptors(byte[] bytes, int start, int count, List<TextureDescriptor> outList)
        {
            int pos = start;
            for (int i = 0; i < count; i++)
            {
                if (pos + 16 > bytes.Length)
                {
                    break;
                }

                ulong texId = ReadU64(bytes, pos);
                uint nextOffset = ReadU32(bytes, pos + 8);
                uint _stringLength = ReadU32(bytes, pos + 12);
                if (!TryReadZString(bytes, pos + 16, 256, out var name) || string.IsNullOrWhiteSpace(name))
                {
                    break;
                }

                outList.Add(new TextureDescriptor(texId, name));

                if (nextOffset != 0)
                {
                    int next = pos + (int)nextOffset;
                    if (next <= 0 || next >= bytes.Length)
                    {
                        break;
                    }
                    pos = next;
                }
                else
                {
                    // Heuristic: descriptors are contiguous when nextOffset is 0.
                    // Move to the next 8-byte aligned boundary after the string.
                    int after = pos + 16 + name.Length + 1;
                    pos = Align(after, 8);
                }
            }
        }

        private sealed class Section
        {
            public required int Position { get; init; }
            public required string Signature { get; init; }
            public required uint SectionSize { get; init; }
            public required uint SubSectionOffset { get; init; }
            public required uint NextSectionOffset { get; init; }
            public required uint BinaryDataOffset { get; init; }
            public required uint SubSectionCount { get; init; }
        }

        private static IEnumerable<Section> EnumerateSectionChain(byte[] bytes, int startOffset)
        {
            int off = startOffset;
            int guard = 0;
            while (off > 0 && off + 32 <= bytes.Length && guard++ < 4096)
            {
                var s = ReadSection(bytes, off);
                yield return s;

                if (s.SubSectionOffset != 0xFFFFFFFF && s.SubSectionOffset != 0 && s.SubSectionCount > 0)
                {
                    int childOff = off + (int)s.SubSectionOffset;
                    foreach (var child in EnumerateSectionChain(bytes, childOff))
                    {
                        yield return child;
                    }
                }

                if (s.NextSectionOffset == 0xFFFFFFFF)
                {
                    break;
                }

                off = off + (int)s.NextSectionOffset;
            }
        }

        private static Section ReadSection(byte[] bytes, int pos)
        {
            string sig = Encoding.ASCII.GetString(bytes, pos, 4);
            uint secSize = ReadU32(bytes, pos + 4);
            uint subOff = ReadU32(bytes, pos + 8);
            uint nextOff = ReadU32(bytes, pos + 12);
            uint _unknown = ReadU32(bytes, pos + 16);
            uint binOff = ReadU32(bytes, pos + 20);
            uint _unknown3 = ReadU32(bytes, pos + 24);
            uint subCnt = ReadU32(bytes, pos + 28);
            return new Section
            {
                Position = pos,
                Signature = sig,
                SectionSize = secSize,
                SubSectionOffset = subOff,
                NextSectionOffset = nextOff,
                BinaryDataOffset = binOff,
                SubSectionCount = subCnt
            };
        }

        private static bool TryReadVec4(byte[] bytes, int pos, out Vector4 v)
        {
            v = default;
            if (pos < 0 || pos + 16 > bytes.Length)
            {
                return false;
            }
            v = new Vector4(
                ReadF32(bytes, pos + 0),
                ReadF32(bytes, pos + 4),
                ReadF32(bytes, pos + 8),
                ReadF32(bytes, pos + 12));
            return true;
        }

        private static bool TryReadZString(byte[] bytes, int pos, int maxLen, out string value)
        {
            value = string.Empty;
            if (pos < 0 || pos >= bytes.Length)
            {
                return false;
            }

            int end = pos;
            int limit = Math.Min(bytes.Length, pos + Math.Max(1, maxLen));
            while (end < limit && bytes[end] != 0)
            {
                end++;
            }
            if (end == pos)
            {
                return false;
            }
            value = Encoding.ASCII.GetString(bytes, pos, end - pos);
            return true;
        }

        private static int Align(int value, int alignment)
        {
            int mask = alignment - 1;
            return (value + mask) & ~mask;
        }

        private static uint Sig(string s)
        {
            var b = Encoding.ASCII.GetBytes(s);
            return BinaryPrimitives.ReadUInt32LittleEndian(b);
        }

        private static uint ReadU32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o, 4));
        private static ushort ReadU16(byte[] b, int o) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o, 2));
        private static ulong ReadU64(byte[] b, int o) => BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(o, 8));
        private static float ReadF32(byte[] b, int o) => BitConverter.ToSingle(b, o);

        private static bool TrySlice(byte[] b, int start, int length, out ReadOnlySpan<byte> slice)
        {
            slice = default;
            if (start < 0 || length < 0 || start + length > b.Length)
            {
                return false;
            }
            slice = new ReadOnlySpan<byte>(b, start, length);
            return true;
        }
    }
}
