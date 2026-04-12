using System.Buffers.Binary;
using System.Text;

namespace TrinitySceneView;

internal sealed class EventMessageResolver
{
    private readonly string assetRoot;
    private readonly string language;

    private readonly Dictionary<string, MessageTable> tableCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> keyToTblCache = new(StringComparer.Ordinal);

    public EventMessageResolver(string assetRoot, string language)
    {
        this.assetRoot = assetRoot;
        this.language = language;
    }

    public bool TryResolve(string msgKey, out string text)
    {
        text = "";
        if (string.IsNullOrWhiteSpace(msgKey)) return false;

        var tblPath = GetOrFindTblForKey(msgKey);
        if (tblPath == null) return false;

        if (!tableCache.TryGetValue(tblPath, out var table))
        {
            table = MessageTable.Load(tblPath);
            tableCache[tblPath] = table;
        }

        if (!table.TryGetString(msgKey, out var raw)) return false;
        text = NormalizeMessageText(raw);
        return true;
    }

    private string? GetOrFindTblForKey(string msgKey)
    {
        if (keyToTblCache.TryGetValue(msgKey, out var cached)) return cached;

        var guess = TryGuessTblPath(msgKey);
        if (guess != null)
        {
            keyToTblCache[msgKey] = guess;
            return guess;
        }

        var found = TryScanForTblPath(msgKey);
        keyToTblCache[msgKey] = found;
        return found;
    }

    private string? TryGuessTblPath(string msgKey)
    {
        if (TryExtractNumberAfterPrefix(msgKey, "msg_Ev_sub_", out var subId) ||
            TryExtractNumberAfterPrefix(msgKey, "msg_sub_", out subId))
        {
            var p = Path.Combine(assetRoot, "ik_message", "dat", language, "sk", $"sub_{subId:000}.tbl");
            return File.Exists(p) ? p : null;
        }

        if (TryExtractNumberAfterPrefix(msgKey, "msg_Ev_d", out var dlcId))
        {
            // msg_Ev_d06_* -> ik_message/dat/English/sk/main_d_06.tbl
            var p = Path.Combine(assetRoot, "ik_message", "dat", language, "sk", $"main_d_{dlcId:00}.tbl");
            return File.Exists(p) ? p : null;
        }

        return null;
    }

    private string? TryScanForTblPath(string msgKey)
    {
        try
        {
            var root = Path.Combine(assetRoot, "ik_message", "dat", language);
            if (!Directory.Exists(root)) return null;

            foreach (var tblPath in Directory.EnumerateFiles(root, "*.tbl", SearchOption.AllDirectories))
            {
                if (!File.Exists(tblPath.Replace(".tbl", ".dat", StringComparison.OrdinalIgnoreCase)))
                    continue;

                IReadOnlyList<string> keys;
                try { keys = AhtbTable.ReadKeys(tblPath); }
                catch { continue; }

                var hit = false;
                for (var i = 0; i < keys.Count; i++)
                {
                    if (string.Equals(keys[i], msgKey, StringComparison.Ordinal))
                    {
                        hit = true;
                        break;
                    }
                }

                if (hit)
                {
                    return tblPath;
                }
            }
        }
        catch { /* ignore */ }

        return null;
    }

    private static bool TryExtractNumberAfterPrefix(string s, string prefix, out int number)
    {
        number = 0;
        if (!s.StartsWith(prefix, StringComparison.Ordinal)) return false;

        var i = prefix.Length;
        var start = i;
        while (i < s.Length && char.IsDigit(s[i])) i++;
        if (i == start) return false;

        var span = s.AsSpan(start, i - start);
        return int.TryParse(span, out number);
    }

    public static string NormalizeMessageText(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";

        // Strip common inline tag sequences starting with 0x10.
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch == (char)0x10)
            {
                i++;
                var skipped = 0;
                while (i < s.Length && skipped < 8)
                {
                    if (s[i] == (char)0x07) break;
                    i++;
                    skipped++;
                }
                continue;
            }

            if (ch == (char)0x07) continue;
            if (ch == '\r') continue;
            if (char.IsControl(ch) && ch != '\n' && ch != '\t') continue;

            sb.Append(ch);
        }

        return sb.ToString().Trim();
    }

    private sealed class MessageTable
    {
        private readonly Dictionary<string, int> keyToId;
        private readonly MsgDataCache dat;

        private MessageTable(IReadOnlyList<string> keys, MsgDataCache dat)
        {
            this.dat = dat;
            keyToId = new Dictionary<string, int>(keys.Count, StringComparer.Ordinal);
            for (var i = 0; i < keys.Count; i++)
            {
                keyToId[keys[i]] = i;
            }
        }

        public static MessageTable Load(string tblPath)
        {
            var datPath = Path.ChangeExtension(tblPath, ".dat");
            var keys = AhtbTable.ReadKeys(tblPath);
            var dat = MsgDataCache.Load(datPath);
            return new MessageTable(keys, dat);
        }

        public bool TryGetString(string msgKey, out string text)
        {
            text = "";
            if (!keyToId.TryGetValue(msgKey, out var id)) return false;
            try
            {
                text = dat.DecodeString(id);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private static class AhtbTable
    {
        public static IReadOnlyList<string> ReadKeys(string tblPath)
        {
            var b = File.ReadAllBytes(tblPath);
            if (b.Length < 8 || Encoding.ASCII.GetString(b, 0, 4) != "AHTB")
                throw new InvalidDataException($"Not an AHTB table: {tblPath}");

            var count = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(4, 4));
            var off = 8;
            var keys = new List<string>((int)count);
            for (var i = 0; i < count; i++)
            {
                off += 8; // u64 hash
                var slen = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(off, 2));
                off += 2;
                var raw = b.AsSpan(off, slen);
                off += slen;
                if (raw.Length == 0 || raw[^1] != 0) throw new InvalidDataException($"Bad AHTB string terminator: {tblPath}");
                keys.Add(Encoding.UTF8.GetString(raw[..^1]));
            }
            return keys;
        }
    }

    private sealed class MsgDataCache
    {
        private readonly byte[] bytes;
        private readonly ushort numLangs;
        private readonly ushort numStrings;
        private readonly uint reserved;
        private readonly uint[] langOffsets;

        private MsgDataCache(byte[] bytes, ushort numLangs, ushort numStrings, uint reserved, uint[] langOffsets)
        {
            this.bytes = bytes;
            this.numLangs = numLangs;
            this.numStrings = numStrings;
            this.reserved = reserved;
            this.langOffsets = langOffsets;
        }

        public static MsgDataCache Load(string datPath)
        {
            var b = File.ReadAllBytes(datPath);
            if (b.Length < 16) throw new InvalidDataException($"Invalid msg data: {datPath}");
            var numLangs = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(0, 2));
            var numStrings = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(2, 2));
            _ = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(4, 4)); // max block size
            var reserved = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(8, 4));
            var offs = new uint[numLangs];
            for (var i = 0; i < numLangs; i++)
            {
                offs[i] = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(12 + i * 4, 4));
            }
            return new MsgDataCache(b, numLangs, numStrings, reserved, offs);
        }

        public string DecodeString(int strId, int langId = 0)
        {
            if (langId < 0 || langId >= numLangs) throw new ArgumentOutOfRangeException(nameof(langId));
            if (strId < 0 || strId >= numStrings) throw new ArgumentOutOfRangeException(nameof(strId));

            var lang0 = (int)langOffsets[langId];
            var paramOff = lang0 + 4 + strId * 8;
            var ofs = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(paramOff, 4));
            var len = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(paramOff + 4, 2));

            var raw = bytes.AsSpan(lang0 + (int)ofs, len * 2);
            Span<ushort> codes = len <= 2048 ? stackalloc ushort[len] : new ushort[len];
            for (var i = 0; i < len; i++)
            {
                codes[i] = BinaryPrimitives.ReadUInt16LittleEndian(raw.Slice(i * 2, 2));
            }

            if (reserved == 0)
            {
                DecryptInPlace(codes, strId);
            }

            var end = 0;
            for (; end < codes.Length; end++)
            {
                if (codes[end] == 0) break;
            }

            var u16 = new byte[end * 2];
            for (var i = 0; i < end; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(u16.AsSpan(i * 2, 2), codes[i]);
            }
            return Encoding.Unicode.GetString(u16);
        }

        private static void DecryptInPlace(Span<ushort> codes, int strId)
        {
            ushort mask = (ushort)((0x2983 * ((strId & 0xFFFF) + 3)) & 0xFFFF);
            for (var i = 0; i < codes.Length; i++)
            {
                codes[i] = (ushort)(codes[i] ^ mask);
                mask = (ushort)((((mask & 0xE000) >> 13) | ((mask & 0x1FFF) << 3)) & 0xFFFF);
            }
        }
    }
}
