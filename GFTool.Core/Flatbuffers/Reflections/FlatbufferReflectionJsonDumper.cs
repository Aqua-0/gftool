using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trinity.Core.Flatbuffers.Reflections
{
    public static class FlatbufferReflectionJsonDumper
    {
        public enum RootKind
        {
            Table,
            VectorOfTables
        }

        public static string Dump(byte[] flatbufferBytes, ReflectionSchemaContext schemaContext)
        {
            ArgumentNullException.ThrowIfNull(flatbufferBytes);
            ArgumentNullException.ThrowIfNull(schemaContext);

            var span = (ReadOnlySpan<byte>)flatbufferBytes;
            if (span.Length < 8)
            {
                throw new InvalidOperationException("Buffer too small to be a FlatBuffer.");
            }

            int rootPos = checked((int)ReadUOffset(span, 0));
            if (rootPos <= 0 || rootPos >= span.Length)
            {
                throw new InvalidOperationException("Invalid root table offset.");
            }

            var rootObj = ReadTable(span, rootPos, schemaContext.RootTable, schemaContext, depth: 0);
            var opts = new JsonSerializerOptions { WriteIndented = true };
            return rootObj.ToJsonString(opts);
        }

        public static string DumpAuto(byte[] flatbufferBytes, ReflectionSchemaContext schemaContext, out RootKind kind)
        {
            ArgumentNullException.ThrowIfNull(flatbufferBytes);
            ArgumentNullException.ThrowIfNull(schemaContext);

            var span = (ReadOnlySpan<byte>)flatbufferBytes;
            if (span.Length < 8)
            {
                throw new InvalidOperationException("Buffer too small to be a FlatBuffer.");
            }

            int rootPos = checked((int)ReadUOffset(span, 0));
            if (rootPos <= 0 || rootPos >= span.Length)
            {
                throw new InvalidOperationException("Invalid root offset.");
            }

            if (LooksLikeTableRoot(span, rootPos, schemaContext.RootTable))
            {
                kind = RootKind.Table;
                var rootObj = ReadTable(span, rootPos, schemaContext.RootTable, schemaContext, depth: 0);
                var opts = new JsonSerializerOptions { WriteIndented = true };
                return rootObj.ToJsonString(opts);
            }

            kind = RootKind.VectorOfTables;
            var rootArray = ReadRootVectorOfTables(span, rootPos, schemaContext.RootTable, schemaContext);
            var optsArray = new JsonSerializerOptions { WriteIndented = true };
            return rootArray.ToJsonString(optsArray);
        }

        private static bool LooksLikeTableRoot(ReadOnlySpan<byte> span, int tablePos, ReflectionObject tableDef)
        {
            // Validate vtable + expected vtable length based on schema.
            if (tablePos < 4 || tablePos + 4 > span.Length)
            {
                return false;
            }

            int vtableOffset = ReadInt32(span, tablePos);
            if (vtableOffset <= 0 || vtableOffset > tablePos)
            {
                return false;
            }

            int vtablePos = tablePos - vtableOffset;
            if (vtablePos < 0 || vtablePos + 4 > span.Length)
            {
                return false;
            }

            ushort vtableLen = ReadUShort(span, vtablePos);
            ushort objSize = ReadUShort(span, vtablePos + 2);
            if (vtableLen < 4)
            {
                return false;
            }

            if (vtablePos + vtableLen > span.Length)
            {
                return false;
            }

            if (objSize < 4 || tablePos + objSize > span.Length)
            {
                return false;
            }

            ushort maxId = 0;
            bool anyFieldPresent = false;
            var fields = tableDef.Fields ?? Array.Empty<ReflectionField>();
            foreach (var f in fields)
            {
                if (f == null)
                {
                    continue;
                }
                if (f.Id > maxId)
                {
                    maxId = f.Id;
                }

                int entryPos = vtablePos + 4 + f.Id * 2;
                if (entryPos + 2 <= vtablePos + vtableLen)
                {
                    ushort off = ReadUShort(span, entryPos);
                    if (off != 0)
                    {
                        anyFieldPresent = true;
                        if (off >= objSize)
                        {
                            return false;
                        }
                    }
                }
            }

            int expectedMinVtableLen = 4 + 2 * (maxId + 1);
            return vtableLen >= expectedMinVtableLen && anyFieldPresent;
        }

        private static JsonArray ReadRootVectorOfTables(ReadOnlySpan<byte> span, int vecPos, ReflectionObject elemTableDef, ReflectionSchemaContext ctx)
        {
            int len = ReadInt32(span, vecPos);
            if (len < 0)
            {
                throw new InvalidOperationException("Invalid root vector length.");
            }

            int dataPos = vecPos + 4;
            if (len > 1_000_000 || dataPos < 0 || dataPos > span.Length)
            {
                throw new InvalidOperationException("Invalid root vector bounds.");
            }

            long end = (long)dataPos + (long)len * 4L;
            if (end > span.Length)
            {
                throw new InvalidOperationException("Invalid root vector bounds.");
            }

            var array = new JsonArray();
            for (int i = 0; i < len; i++)
            {
                int elemOffsetPos = checked(dataPos + i * 4);
                uint rel = ReadUOffset(span, elemOffsetPos);
                if (rel == 0)
                {
                    array.Add(null);
                    continue;
                }

                int tablePos = checked(elemOffsetPos + (int)rel);
                array.Add(ReadTable(span, tablePos, elemTableDef, ctx, depth: 0));
            }

            return array;
        }

        private static JsonObject ReadTable(ReadOnlySpan<byte> span, int tablePos, ReflectionObject tableDef, ReflectionSchemaContext ctx, int depth)
        {
            if (depth > 96)
            {
                throw new InvalidOperationException("FlatBuffer nesting is too deep.");
            }

            var obj = new JsonObject();
            var fields = tableDef.Fields ?? Array.Empty<ReflectionField>();

            int vtableOffset = ReadInt32(span, tablePos);
            int vtablePos = tablePos - vtableOffset;
            if (vtablePos < 0 || vtablePos + 4 > span.Length)
            {
                throw new InvalidOperationException("Invalid vtable position.");
            }

            ushort vtableLen = ReadUShort(span, vtablePos);
            int vtableEnd = vtablePos + vtableLen;

            foreach (var field in fields)
            {
                if (field?.Type == null || string.IsNullOrWhiteSpace(field.Name))
                {
                    continue;
                }

                int fieldOffset = GetVtableFieldOffset(span, vtablePos, vtableEnd, field.Id);
                if (fieldOffset == 0)
                {
                    continue;
                }

                int fieldPos = tablePos + fieldOffset;
                JsonNode? valueNode;
                if (field.Type.BaseType == ReflectionBaseType.Union)
                {
                    byte utype = 0;
                    if (TryFindUnionTypeField(fields, field.Name!, out var typeField) && typeField?.Type != null)
                    {
                        int typeOffset = GetVtableFieldOffset(span, vtablePos, vtableEnd, typeField.Id);
                        if (typeOffset != 0)
                        {
                            int typePos = tablePos + typeOffset;
                            utype = typeField.Type.BaseType switch
                            {
                                ReflectionBaseType.UType => span[typePos],
                                ReflectionBaseType.UByte => span[typePos],
                                ReflectionBaseType.Byte => unchecked((byte)span[typePos]),
                                _ => (byte)0
                            };
                        }
                    }

                    valueNode = ReadUnionByTypeNode(span, fieldPos, field.Type, utype, ctx, depth + 1);
                }
                else
                {
                    valueNode = ReadValue(span, fieldPos, field.Type, ctx, depth + 1);
                }
                if (valueNode != null)
                {
                    obj[field.Name!] = valueNode;
                }
            }

            return obj;
        }

        private static bool TryFindUnionTypeField(ReflectionField[] fields, string unionFieldName, out ReflectionField? typeField)
        {
            typeField = null;
            if (string.IsNullOrWhiteSpace(unionFieldName))
            {
                return false;
            }

            string a = unionFieldName + "Type";
            string b = unionFieldName + "_type";
            for (int i = 0; i < fields.Length; i++)
            {
                var f = fields[i];
                if (f == null || string.IsNullOrWhiteSpace(f.Name) || f.Type == null)
                {
                    continue;
                }

                if (string.Equals(f.Name, a, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(f.Name, b, StringComparison.OrdinalIgnoreCase))
                {
                    typeField = f;
                    return true;
                }
            }

            return false;
        }

        private static int GetVtableFieldOffset(ReadOnlySpan<byte> span, int vtablePos, int vtableEnd, ushort id)
        {
            int entryPos = vtablePos + 4 + id * 2;
            if (entryPos + 2 > vtableEnd)
            {
                return 0;
            }

            return ReadUShort(span, entryPos);
        }

        private static JsonNode? ReadValue(ReadOnlySpan<byte> span, int valuePos, ReflectionType type, ReflectionSchemaContext ctx, int depth)
        {
            switch (type.BaseType)
            {
                case ReflectionBaseType.Bool:
                    return JsonValue.Create(span[valuePos] != 0);
                case ReflectionBaseType.Byte:
                    return JsonValue.Create(unchecked((sbyte)span[valuePos]));
                case ReflectionBaseType.UByte:
                case ReflectionBaseType.UType:
                    return JsonValue.Create(span[valuePos]);
                case ReflectionBaseType.Short:
                    return JsonValue.Create(ReadShort(span, valuePos));
                case ReflectionBaseType.UShort:
                    return JsonValue.Create(ReadUShort(span, valuePos));
                case ReflectionBaseType.Int:
                    return JsonValue.Create(ReadInt32(span, valuePos));
                case ReflectionBaseType.UInt:
                    return JsonValue.Create(ReadUInt32(span, valuePos));
                case ReflectionBaseType.Long:
                    return JsonValue.Create(ReadInt64(span, valuePos));
                case ReflectionBaseType.ULong:
                    return JsonValue.Create(ReadUInt64(span, valuePos));
                case ReflectionBaseType.Float:
                    return JsonValue.Create(ReadSingle(span, valuePos));
                case ReflectionBaseType.Double:
                    return JsonValue.Create(ReadDouble(span, valuePos));
                case ReflectionBaseType.String:
                    return ReadStringNode(span, valuePos);
                case ReflectionBaseType.Obj:
                    return ReadObjectNode(span, valuePos, type, ctx, depth);
                case ReflectionBaseType.Vector:
                    return ReadVectorNode(span, valuePos, type, ctx, depth);
                case ReflectionBaseType.Union:
                    // Union values require the associated UType field to interpret the payload.
                    return ReadUnionAsTableNode(span, valuePos, type, ctx, depth);
                default:
                    return null;
            }
        }

        private static JsonNode? ReadStringNode(ReadOnlySpan<byte> span, int offsetFieldPos)
        {
            uint rel = ReadUOffset(span, offsetFieldPos);
            if (rel == 0)
            {
                return null;
            }

            int strPos = checked(offsetFieldPos + (int)rel);
            int len = ReadInt32(span, strPos);
            if (len < 0 || strPos + 4 + len > span.Length)
            {
                throw new InvalidOperationException("Invalid string length.");
            }

            string s = Encoding.UTF8.GetString(span.Slice(strPos + 4, len));
            return JsonValue.Create(s);
        }

        private static JsonNode? ReadObjectNode(ReadOnlySpan<byte> span, int valuePos, ReflectionType type, ReflectionSchemaContext ctx, int depth)
        {
            if (!ctx.ObjectsByIndex.TryGetValue(type.Index, out var objDef))
            {
                return null;
            }

            if (objDef.IsStruct)
            {
                return ReadStruct(span, valuePos, objDef, ctx, depth);
            }

            uint rel = ReadUOffset(span, valuePos);
            if (rel == 0)
            {
                return null;
            }

            int tablePos = checked(valuePos + (int)rel);
            return ReadTable(span, tablePos, objDef, ctx, depth);
        }

        private static JsonNode ReadStruct(ReadOnlySpan<byte> span, int structPos, ReflectionObject structDef, ReflectionSchemaContext ctx, int depth)
        {
            var obj = new JsonObject();
            var fields = structDef.Fields ?? Array.Empty<ReflectionField>();

            foreach (var field in fields)
            {
                if (field?.Type == null || string.IsNullOrWhiteSpace(field.Name))
                {
                    continue;
                }

                int fieldPos = structPos + field.Offset;
                var value = ReadValue(span, fieldPos, field.Type, ctx, depth + 1);
                if (value != null)
                {
                    obj[field.Name!] = value;
                }
            }

            return obj;
        }

        private static JsonNode? ReadVectorNode(ReadOnlySpan<byte> span, int valuePos, ReflectionType type, ReflectionSchemaContext ctx, int depth)
        {
            uint rel = ReadUOffset(span, valuePos);
            if (rel == 0)
            {
                return null;
            }

            int vecPos = checked(valuePos + (int)rel);
            int len = ReadInt32(span, vecPos);
            if (len < 0)
            {
                throw new InvalidOperationException("Invalid vector length.");
            }

            int dataPos = vecPos + 4;
            var array = new JsonArray();

            if (type.Element == ReflectionBaseType.Obj && ctx.ObjectsByIndex.TryGetValue(type.Index, out var objDef) && objDef.IsStruct)
            {
                int elemSize = objDef.Bytesize;
                for (int i = 0; i < len; i++)
                {
                    int elemPos = checked(dataPos + i * elemSize);
                    array.Add(ReadStruct(span, elemPos, objDef, ctx, depth + 1));
                }
                return array;
            }

            int elemSizeScalar = ScalarSize(type.Element);
            if (elemSizeScalar > 0)
            {
                for (int i = 0; i < len; i++)
                {
                    int elemPos = checked(dataPos + i * elemSizeScalar);
                    array.Add(ReadScalar(span, elemPos, type.Element));
                }
                return array;
            }

            // Offsets: strings, tables, nested vectors, unions.
            for (int i = 0; i < len; i++)
            {
                int elemPos = checked(dataPos + i * 4);
                array.Add(ReadVectorElementByOffset(span, elemPos, type, ctx, depth + 1));
            }

            return array;
        }

        private static JsonNode? ReadVectorElementByOffset(ReadOnlySpan<byte> span, int elemOffsetPos, ReflectionType vectorType, ReflectionSchemaContext ctx, int depth)
        {
            switch (vectorType.Element)
            {
                case ReflectionBaseType.String:
                    return ReadStringNode(span, elemOffsetPos);
                case ReflectionBaseType.Obj:
                {
                    if (!ctx.ObjectsByIndex.TryGetValue(vectorType.Index, out var objDef))
                    {
                        return null;
                    }

                    uint rel = ReadUOffset(span, elemOffsetPos);
                    if (rel == 0)
                    {
                        return null;
                    }
                    int tablePos = checked(elemOffsetPos + (int)rel);
                    return ReadTable(span, tablePos, objDef, ctx, depth);
                }
                case ReflectionBaseType.Vector:
                {
                    // Nested vectors are rare; treat as a vector with the same index/element.
                    var nested = new ReflectionType
                    {
                        BaseType = ReflectionBaseType.Vector,
                        Element = vectorType.BaseType,
                        Index = vectorType.Index,
                        FixedLength = vectorType.FixedLength
                    };
                    return ReadVectorNode(span, elemOffsetPos, nested, ctx, depth);
                }
                default:
                    return null;
            }
        }

        private static JsonNode? ReadUnionAsTableNode(ReadOnlySpan<byte> span, int valuePos, ReflectionType type, ReflectionSchemaContext ctx, int depth)
        {
            uint rel = ReadUOffset(span, valuePos);
            if (rel == 0)
            {
                return null;
            }

            int tablePos = checked(valuePos + (int)rel);
            if (!ctx.ObjectsByIndex.TryGetValue(type.Index, out var objDef))
            {
                return null;
            }

            return ReadTable(span, tablePos, objDef, ctx, depth);
        }

        private static JsonNode? ReadUnionByTypeNode(ReadOnlySpan<byte> span, int valuePos, ReflectionType unionType, byte utype, ReflectionSchemaContext ctx, int depth)
        {
            if (utype == 0)
            {
                return null;
            }

            uint rel = ReadUOffset(span, valuePos);
            if (rel == 0)
            {
                return null;
            }

            int tablePos = checked(valuePos + (int)rel);

            if (!ctx.EnumsByIndex.TryGetValue(unionType.Index, out var unionEnum))
            {
                return null;
            }

            string? unionValName = null;
            var vals = unionEnum.Values ?? Array.Empty<ReflectionEnumVal>();
            for (int i = 0; i < vals.Length; i++)
            {
                var v = vals[i];
                if (v != null && v.Value == utype && !string.IsNullOrWhiteSpace(v.Name))
                {
                    unionValName = v.Name;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(unionValName))
            {
                return null;
            }

            if (!ctx.ObjectsByName.TryGetValue(unionValName, out var objDef))
            {
                // Fallback: union enums often use unqualified names; object names may be qualified.
                foreach (var kv in ctx.ObjectsByName)
                {
                    if (kv.Key.EndsWith("." + unionValName, StringComparison.Ordinal) ||
                        kv.Key.EndsWith(unionValName, StringComparison.Ordinal))
                    {
                        objDef = kv.Value;
                        break;
                    }
                }
            }

            if (objDef == null)
            {
                return null;
            }

            return ReadTable(span, tablePos, objDef, ctx, depth);
        }

        private static JsonNode ReadScalar(ReadOnlySpan<byte> span, int pos, ReflectionBaseType t)
        {
            return t switch
            {
                ReflectionBaseType.Bool => JsonValue.Create(span[pos] != 0)!,
                ReflectionBaseType.Byte => JsonValue.Create(unchecked((sbyte)span[pos]))!,
                ReflectionBaseType.UByte or ReflectionBaseType.UType => JsonValue.Create(span[pos])!,
                ReflectionBaseType.Short => JsonValue.Create(ReadShort(span, pos))!,
                ReflectionBaseType.UShort => JsonValue.Create(ReadUShort(span, pos))!,
                ReflectionBaseType.Int => JsonValue.Create(ReadInt32(span, pos))!,
                ReflectionBaseType.UInt => JsonValue.Create(ReadUInt32(span, pos))!,
                ReflectionBaseType.Long => JsonValue.Create(ReadInt64(span, pos))!,
                ReflectionBaseType.ULong => JsonValue.Create(ReadUInt64(span, pos))!,
                ReflectionBaseType.Float => JsonValue.Create(ReadSingle(span, pos))!,
                ReflectionBaseType.Double => JsonValue.Create(ReadDouble(span, pos))!,
                _ => JsonValue.Create((int)0)!
            };
        }

        private static int ScalarSize(ReflectionBaseType t)
        {
            return t switch
            {
                ReflectionBaseType.Bool => 1,
                ReflectionBaseType.Byte => 1,
                ReflectionBaseType.UByte => 1,
                ReflectionBaseType.UType => 1,
                ReflectionBaseType.Short => 2,
                ReflectionBaseType.UShort => 2,
                ReflectionBaseType.Int => 4,
                ReflectionBaseType.UInt => 4,
                ReflectionBaseType.Float => 4,
                ReflectionBaseType.Long => 8,
                ReflectionBaseType.ULong => 8,
                ReflectionBaseType.Double => 8,
                _ => 0
            };
        }

        private static short ReadShort(ReadOnlySpan<byte> span, int offset) => BinaryPrimitives.ReadInt16LittleEndian(span.Slice(offset, 2));
        private static ushort ReadUShort(ReadOnlySpan<byte> span, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset, 2));
        private static int ReadInt32(ReadOnlySpan<byte> span, int offset) => BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
        private static uint ReadUInt32(ReadOnlySpan<byte> span, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4));
        private static long ReadInt64(ReadOnlySpan<byte> span, int offset) => BinaryPrimitives.ReadInt64LittleEndian(span.Slice(offset, 8));
        private static ulong ReadUInt64(ReadOnlySpan<byte> span, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(offset, 8));

        private static float ReadSingle(ReadOnlySpan<byte> span, int offset) => BitConverter.Int32BitsToSingle(ReadInt32(span, offset));
        private static double ReadDouble(ReadOnlySpan<byte> span, int offset) => BitConverter.Int64BitsToDouble(ReadInt64(span, offset));

        private static uint ReadUOffset(ReadOnlySpan<byte> span, int offset) => ReadUInt32(span, offset);
    }
}
