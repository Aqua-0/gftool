using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private static void ParseTracpDefaultsFromBytes(ReadOnlySpan<byte> bytes, Dictionary<string, object> defaults)
        {
            var fb = new FbReader(bytes);
            int root = fb.RootTable();
            int animParamCfg = root;
            int paramCfg = fb.ReadTableOffset(animParamCfg, fieldIndex: 0);
            if (paramCfg == 0)
            {
                return;
            }

            ReadDefaults_IntParameters(fb, paramCfg, fieldIndex: 0, defaults);
            ReadDefaults_FloatParameters(fb, paramCfg, fieldIndex: 1, defaults);
            ReadDefaults_BoolParameters(fb, paramCfg, fieldIndex: 2, defaults);
            ReadDefaults_TriggerParameters(fb, paramCfg, fieldIndex: 3, defaults);
        }

        private static void ReadDefaults_IntParameters(FbReader fb, int paramCfg, int fieldIndex, Dictionary<string, object> defaults)
        {
            foreach (var el in fb.ReadVectorOfTables(paramCfg, fieldIndex))
            {
                string name = fb.ReadString(el, fieldIndex: 0) ?? "";
                if (name.Length == 0)
                {
                    continue;
                }
                int dv = fb.ReadInt32(el, fieldIndex: 3);
                defaults[name] = dv;
            }
        }

        private static void ReadDefaults_FloatParameters(FbReader fb, int paramCfg, int fieldIndex, Dictionary<string, object> defaults)
        {
            foreach (var el in fb.ReadVectorOfTables(paramCfg, fieldIndex))
            {
                string name = fb.ReadString(el, fieldIndex: 0) ?? "";
                if (name.Length == 0)
                {
                    continue;
                }
                float dv = fb.ReadFloat32(el, fieldIndex: 3);
                defaults[name] = (double)dv;
            }
        }

        private static void ReadDefaults_BoolParameters(FbReader fb, int paramCfg, int fieldIndex, Dictionary<string, object> defaults)
        {
            foreach (var el in fb.ReadVectorOfTables(paramCfg, fieldIndex))
            {
                string name = fb.ReadString(el, fieldIndex: 0) ?? "";
                if (name.Length == 0)
                {
                    continue;
                }
                bool dv = fb.ReadBool(el, fieldIndex: 1);
                defaults[name] = dv;
            }
        }

        private static void ReadDefaults_TriggerParameters(FbReader fb, int paramCfg, int fieldIndex, Dictionary<string, object> defaults)
        {
            foreach (var el in fb.ReadVectorOfTables(paramCfg, fieldIndex))
            {
                string name = fb.ReadString(el, fieldIndex: 0) ?? "";
                if (name.Length == 0)
                {
                    continue;
                }
                defaults[name] = false;
            }
        }

        private static void ParseTracrResourcesFromBytes(ReadOnlySpan<byte> bytes, Dictionary<string, string> resourceToTranmRel)
        {
            var fb = new FbReader(bytes);
            int root = fb.RootTable();
            int animResCfg = root;
            int resCfg = fb.ReadTableOffset(animResCfg, fieldIndex: 0);
            if (resCfg == 0)
            {
                return;
            }

            foreach (var fr in fb.ReadVectorOfTables(resCfg, fieldIndex: 0))
            {
                string name = fb.ReadString(fr, fieldIndex: 0) ?? "";
                if (name.Length == 0)
                {
                    continue;
                }

                string? path = null;

                int clipData = fb.ReadTableOffset(fr, fieldIndex: 3);
                if (clipData != 0)
                {
                    int joint = fb.ReadTableOffset(clipData, fieldIndex: 0);
                    if (joint != 0)
                    {
                        path = fb.ReadString(joint, fieldIndex: 0);
                    }
                }

                if (string.IsNullOrWhiteSpace(path))
                {
                    path = fb.ReadString(fr, fieldIndex: 1);
                }

                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                path = path.Replace('\\', '/').TrimStart('/');
                resourceToTranmRel[name] = path;
            }
        }

        private static void ParseTracsStatesFromBytes(ReadOnlySpan<byte> bytes, Dictionary<string, TracsState> outStates, out string? entryStateName)
        {
            entryStateName = null;

            var fb = new FbReader(bytes);
            int root = fb.RootTable();
            int animStateCfg = root;
            int stateCfg = fb.ReadTableOffset(animStateCfg, fieldIndex: 0);
            if (stateCfg == 0)
            {
                return;
            }

            foreach (var top in fb.ReadVectorOfTables(stateCfg, fieldIndex: 0))
            {
                var layerName = fb.ReadString(top, fieldIndex: 0);
                if (!string.Equals(layerName, "Base", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int st = fb.ReadTableOffset(top, fieldIndex: 1);
                if (st != 0)
                {
                    WalkTracsState(fb, st, outStates, ref entryStateName);
                }
                break;
            }
        }

        private static void WalkTracsState(FbReader fb, int state, Dictionary<string, TracsState> outStates, ref string? entryStateName)
        {
            string name = fb.ReadString(state, fieldIndex: 0) ?? "";
            uint typeU = fb.ReadUInt32(state, fieldIndex: 1);
            string type = StateTypeToString(typeU);

            string? resName = null;
            string? resType = null;
            int src = fb.ReadTableOffset(state, fieldIndex: 3);
            if (src != 0)
            {
                int rl = fb.ReadTableOffset(src, fieldIndex: 0);
                if (rl != 0)
                {
                    resName = fb.ReadString(rl, fieldIndex: 0);
                    uint rt = fb.ReadUInt32(rl, fieldIndex: 1);
                    resType = ResourceTypeToString(rt);
                }
            }

            var transitions = new List<TracsTransition>();
            foreach (var tr in fb.ReadVectorOfTables(state, fieldIndex: 4))
            {
                string to = fb.ReadString(tr, fieldIndex: 0) ?? "";
                if (to.Length == 0)
                {
                    continue;
                }

                bool hasExitTime = fb.ReadBool(tr, fieldIndex: 1);
                float exitTime = fb.ReadFloat32(tr, fieldIndex: 2);
                string? exitTimeParam = fb.ReadString(tr, fieldIndex: 10);
                if (string.IsNullOrWhiteSpace(exitTimeParam))
                {
                    exitTimeParam = null;
                }

                uint lopU = fb.ReadUInt32(tr, fieldIndex: 14);
                string lop = LogicalOperatorToString(lopU);
                int priority = fb.ReadInt32(tr, fieldIndex: 13);
                string? requiredLastState = fb.ReadString(tr, fieldIndex: 16);
                if (string.IsNullOrWhiteSpace(requiredLastState))
                {
                    requiredLastState = null;
                }

                var conds = new List<TracsCondition>();
                foreach (var c in fb.ReadVectorOfTables(tr, fieldIndex: 6))
                {
                    string pn = fb.ReadString(c, fieldIndex: 0) ?? "";
                    if (pn.Length == 0)
                    {
                        continue;
                    }

                    uint funcU = fb.ReadUInt32(c, fieldIndex: 1);
                    string func = ConditionFuncToString(funcU);

                    if (!TryReadConditionUnionValue(fb, c, out var valType, out var value))
                    {
                        continue;
                    }

                    uint copU = fb.ReadUInt32(c, fieldIndex: 4);
                    string cop = LogicalOperatorToString(copU);
                    conds.Add(new TracsCondition(pn, func, valType, value, cop));
                }

                transitions.Add(new TracsTransition(to, lop, priority, hasExitTime, exitTime, exitTimeParam, requiredLastState, conds));
            }

            if (name.Length > 0 && !outStates.ContainsKey(name))
            {
                outStates[name] = new TracsState(name, type, resName, resType, transitions);
            }

            if (entryStateName == null && string.Equals(type, "Entry", StringComparison.OrdinalIgnoreCase) && name.EndsWith("/Entry", StringComparison.OrdinalIgnoreCase))
            {
                entryStateName = name;
            }

            foreach (var sub in fb.ReadVectorOfTables(state, fieldIndex: 5))
            {
                WalkTracsState(fb, sub, outStates, ref entryStateName);
            }
        }

        private static bool TryReadConditionUnionValue(FbReader fb, int condition, out string valueType, out object value)
        {
            valueType = string.Empty;
            value = 0;

            // union ConditionValue { Int, Float, Bool, Trigger }
            // FlatBuffers inserts: value_type then value.
            byte vt = fb.ReadUInt8(condition, fieldIndex: 2);
            int v = fb.ReadTableOffset(condition, fieldIndex: 3);
            if (vt == 0 || v == 0)
            {
                return false;
            }

            switch (vt)
            {
                case 1:
                    valueType = "ConditionValue_Int";
                    value = fb.ReadInt32(v, fieldIndex: 0);
                    return true;
                case 2:
                    valueType = "ConditionValue_Float";
                    value = (double)fb.ReadFloat32(v, fieldIndex: 0);
                    return true;
                case 3:
                    valueType = "ConditionValue_Bool";
                    value = fb.ReadBool(v, fieldIndex: 0);
                    return true;
                case 4:
                    valueType = "ConditionValue_Trigger";
                    value = fb.ReadBool(v, fieldIndex: 0);
                    return true;
                default:
                    return false;
            }
        }

        private static string StateTypeToString(uint v) => v switch
        {
            0 => "Normal",
            1 => "Entry",
            2 => "Exit",
            3 => "Any",
            4 => "Top",
            _ => v.ToString()
        };

        private static string ResourceTypeToString(uint v) => v switch
        {
            0 => "File",
            1 => "Blend1D",
            2 => "Blend2D",
            3 => "Null",
            _ => v.ToString()
        };

        private static string ConditionFuncToString(uint v) => v switch
        {
            0 => "Less",
            1 => "LEqual",
            2 => "Greater",
            3 => "GEqual",
            4 => "Equal",
            5 => "NotEqual",
            _ => v.ToString()
        };

        private static string LogicalOperatorToString(uint v) => v switch
        {
            0 => "And",
            1 => "Or",
            _ => "And"
        };

        private readonly ref struct FbReader
        {
            private readonly ReadOnlySpan<byte> data;

            public FbReader(ReadOnlySpan<byte> data) => this.data = data;

            public int RootTable()
            {
                int off = checked((int)ReadUOffset(0));
                return off;
            }

            public int ReadTableOffset(int table, int fieldIndex)
            {
                int fieldPos = GetFieldPos(table, fieldIndex);
                if (fieldPos == 0)
                {
                    return 0;
                }
                uint rel = ReadUOffset(fieldPos);
                return rel == 0 ? 0 : checked((int)(fieldPos + rel));
            }

            public string? ReadString(int table, int fieldIndex)
            {
                int fieldPos = GetFieldPos(table, fieldIndex);
                if (fieldPos == 0)
                {
                    return null;
                }
                uint rel = ReadUOffset(fieldPos);
                if (rel == 0)
                {
                    return null;
                }
                int str = checked((int)(fieldPos + rel));
                int len = checked((int)ReadUOffset(str));
                int start = str + 4;
                if (len <= 0 || start + len > data.Length)
                {
                    return "";
                }
                return Encoding.UTF8.GetString(data.Slice(start, len));
            }

            public int ReadInt32(int table, int fieldIndex)
            {
                int pos = GetFieldPos(table, fieldIndex);
                return pos == 0 ? 0 : BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos, 4));
            }

            public uint ReadUInt32(int table, int fieldIndex)
            {
                int pos = GetFieldPos(table, fieldIndex);
                return pos == 0 ? 0u : BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos, 4));
            }

            public float ReadFloat32(int table, int fieldIndex)
            {
                int pos = GetFieldPos(table, fieldIndex);
                return pos == 0 ? 0.0f : BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos, 4)));
            }

            public bool ReadBool(int table, int fieldIndex)
            {
                int pos = GetFieldPos(table, fieldIndex);
                return pos != 0 && data[pos] != 0;
            }

            public byte ReadUInt8(int table, int fieldIndex)
            {
                int pos = GetFieldPos(table, fieldIndex);
                return pos == 0 ? (byte)0 : data[pos];
            }

            public IEnumerable<int> ReadVectorOfTables(int table, int fieldIndex)
            {
                var list = new List<int>();

                int fieldPos = GetFieldPos(table, fieldIndex);
                if (fieldPos == 0)
                {
                    return list;
                }

                uint rel = ReadUOffset(fieldPos);
                if (rel == 0)
                {
                    return list;
                }
                int vec = checked((int)(fieldPos + rel));
                int len = checked((int)ReadUOffset(vec));
                int start = vec + 4;
                for (int i = 0; i < len; i++)
                {
                    int elPos = start + i * 4;
                    if (elPos + 4 > data.Length)
                    {
                        return list;
                    }
                    uint elRel = ReadUOffset(elPos);
                    if (elRel == 0)
                    {
                        continue;
                    }
                    list.Add(checked((int)(elPos + elRel)));
                }

                return list;
            }

            private int GetFieldPos(int table, int fieldIndex)
            {
                if (table <= 0 || table + 4 > data.Length)
                {
                    return 0;
                }

                int vtOff = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(table, 4));
                int vt = table - vtOff;
                if (vt < 0 || vt + 4 > data.Length)
                {
                    return 0;
                }

                ushort vtLen = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(vt, 2));
                int fieldEntry = vt + 4 + fieldIndex * 2;
                if (fieldEntry + 2 > vt + vtLen || fieldEntry + 2 > data.Length)
                {
                    return 0;
                }

                ushort off = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(fieldEntry, 2));
                return off == 0 ? 0 : table + off;
            }

            private uint ReadUOffset(int pos)
            {
                if (pos < 0 || pos + 4 > data.Length)
                {
                    return 0;
                }
                return BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos, 4));
            }
        }
    }
}
