using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GFTool.Renderer.Scene.GraphicsObjects;
using OpenTK.Mathematics;
using Trinity.Core.Flatbuffers.TR.Model;
using Trinity.Core.Flatbuffers.Utils;
using Trinity.Core.Utils;

namespace TrinityModelViewer.Export
{
    internal static class TrmmtBinaryPatcher
    {
        // Schema-based vtable indices for the TRMMT "metadata/variation" flavor (SV/ZA) used by the viewer.
        // This patcher updates only selection indices and per-variation parameter values, preserving all other bytes.

        private const int Root_ItemList = 2;

        private const int Item_Name = 0;
        private const int Item_ParamList = 3;

        private const int MetaParam_Name = 0;
        private const int MetaParam_OverrideDefaultValue = 8;
        private const int MetaParam_UseNoAnime = 9;
        private const int MetaParam_NoAnimeParam = 10;

        private const int NoAnime_VariationCount = 0;
        private const int NoAnime_MaterialList = 1;

        private const int MetaMaterial_Name = 0;
        private const int MetaMaterial_FloatList = 1;
        private const int MetaMaterial_Float3List = 2;
        private const int MetaMaterial_Float4List = 3;
        private const int MetaMaterial_IntList = 4;

        private const int ParamTable_Name = 0;
        private const int ParamTable_Values = 1;

        public static void ExportEditedTrmmtPreserveAllFields(
            string sourceTrmmtPath,
            Model model,
            string outputTrmmtPath,
            IReadOnlyList<Model.NewMaterialCloneRequest>? cloneRequests = null)
        {
            if (string.IsNullOrWhiteSpace(sourceTrmmtPath)) throw new ArgumentException("Missing source TRMMT path.", nameof(sourceTrmmtPath));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (string.IsNullOrWhiteSpace(outputTrmmtPath)) throw new ArgumentException("Missing output TRMMT path.", nameof(outputTrmmtPath));
            if (!File.Exists(sourceTrmmtPath)) throw new FileNotFoundException("Source TRMMT not found.", sourceTrmmtPath);

            var bytes = BuildEditedTrmmtBytesPreserveAllFields(sourceTrmmtPath, model, cloneRequests, out bool changed);

            Directory.CreateDirectory(Path.GetDirectoryName(outputTrmmtPath) ?? ".");
            if (!changed)
            {
                if (!string.Equals(Path.GetFullPath(sourceTrmmtPath), Path.GetFullPath(outputTrmmtPath), StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(sourceTrmmtPath, outputTrmmtPath, overwrite: true);
                }
                return;
            }

            File.WriteAllBytes(outputTrmmtPath, bytes);
        }

        public static void ExportEditedTrmmtUnsafeReserializeAppend(
            string sourceTrmmtPath,
            Model model,
            string outputTrmmtPath,
            IReadOnlyList<Model.NewMaterialCloneRequest> cloneRequests)
        {
            if (string.IsNullOrWhiteSpace(sourceTrmmtPath)) throw new ArgumentException("Missing source TRMMT path.", nameof(sourceTrmmtPath));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (string.IsNullOrWhiteSpace(outputTrmmtPath)) throw new ArgumentException("Missing output TRMMT path.", nameof(outputTrmmtPath));
            if (!File.Exists(sourceTrmmtPath)) throw new FileNotFoundException("Source TRMMT not found.", sourceTrmmtPath);

            var patched = BuildEditedTrmmtBytesPreserveAllFields(sourceTrmmtPath, model, cloneRequests, out _);

            TrmmtMetadataFile? typed;
            try
            {
                typed = FlatBufferConverter.DeserializeFrom<TrmmtMetadataFile>(patched);
            }
            catch
            {
                typed = null;
            }

            if (typed?.ItemList == null || typed.ItemList.Length == 0)
            {
                ExportEditedTrmmtPreserveAllFields(sourceTrmmtPath, model, outputTrmmtPath, cloneRequests);
                return;
            }

            var unsafeRequests = cloneRequests
                .Where(r => r.TrmmtCloneMode == Model.NewMaterialTrmmtCloneMode.Unsafe &&
                            !string.IsNullOrWhiteSpace(r.TemplateName) &&
                            !string.IsNullOrWhiteSpace(r.NewName))
                .ToArray();

            if (unsafeRequests.Length == 0)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputTrmmtPath) ?? ".");
                File.WriteAllBytes(outputTrmmtPath, patched);
                return;
            }

            foreach (var item in typed.ItemList)
            {
                if (item?.ParamList == null)
                {
                    continue;
                }

                foreach (var param in item.ParamList)
                {
                    if (param == null || !param.UseNoAnime || param.NoAnimeParam?.MaterialList == null)
                    {
                        continue;
                    }

                    var mats = param.NoAnimeParam.MaterialList.ToList();
                    foreach (var req in unsafeRequests)
                    {
                        if (mats.Any(m => m != null && string.Equals(m.MaterialName, req.NewName, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        var templateMat = mats.FirstOrDefault(m => m != null && string.Equals(m.MaterialName, req.TemplateName, StringComparison.OrdinalIgnoreCase));
                        if (templateMat == null)
                        {
                            continue;
                        }

                        mats.Add(CloneTrmmtMetaMaterial(templateMat, req.NewName));
                    }

                    param.NoAnimeParam.MaterialList = mats.ToArray();
                }
            }

            var outBytes = FlatBufferConverter.SerializeFrom(typed);
            Directory.CreateDirectory(Path.GetDirectoryName(outputTrmmtPath) ?? ".");
            File.WriteAllBytes(outputTrmmtPath, outBytes);
        }

        private static byte[] BuildEditedTrmmtBytesPreserveAllFields(
            string sourceTrmmtPath,
            Model model,
            IReadOnlyList<Model.NewMaterialCloneRequest>? cloneRequests,
            out bool changed)
        {
            var data = File.ReadAllBytes(sourceTrmmtPath);
            var fb = new FlatBufferBinary(data);
            changed = PatchTrmmtInPlace(fb, model, cloneRequests);
            return fb.Buffer;
        }

        private static bool PatchTrmmtInPlace(FlatBufferBinary fb, Model model, IReadOnlyList<Model.NewMaterialCloneRequest>? cloneRequests)
        {
            var selectionMap = BuildSelectionMap(model.GetMaterialMetadataSelectionsSnapshot());
            var overrideGroups = BuildOverrideGroups(model.GetMaterialMetadataValueOverridesSnapshot());
            var safeClone = (cloneRequests ?? Array.Empty<Model.NewMaterialCloneRequest>())
                .Where(r => r.TrmmtCloneMode == Model.NewMaterialTrmmtCloneMode.Safe &&
                            !string.IsNullOrWhiteSpace(r.TemplateName) &&
                            !string.IsNullOrWhiteSpace(r.NewName))
                .ToArray();

            if (selectionMap.Count == 0 && overrideGroups.Count == 0 && safeClone.Length == 0)
            {
                return false;
            }

            int root = fb.GetRootTableOffset();
            int itemListField = fb.GetFieldAbsoluteOffset(root, Root_ItemList);
            if (itemListField == 0)
            {
                return false;
            }

            int itemVec = fb.GetVectorDataStartFromUOffsetField(itemListField, out int itemCount);
            if (itemVec == 0 || itemCount <= 0)
            {
                return false;
            }

            bool anyPatched = false;

            for (int itemIndex = 0; itemIndex < itemCount; itemIndex++)
            {
                int item = fb.GetVectorElementTableOffset(itemVec, itemIndex);
                if (item == 0)
                {
                    continue;
                }

                int paramListField = fb.GetFieldAbsoluteOffset(item, Item_ParamList);
                if (paramListField == 0)
                {
                    // Not the "variation metadata" flavor.
                    continue;
                }

                string setName = ReadSetName(fb, item, itemIndex);

                int paramVec = fb.GetVectorDataStartFromUOffsetField(paramListField, out int paramCount);
                if (paramVec == 0 || paramCount <= 0)
                {
                    continue;
                }

                for (int p = 0; p < paramCount; p++)
                {
                    int param = fb.GetVectorElementTableOffset(paramVec, p);
                    if (param == 0)
                    {
                        continue;
                    }

                    string paramName = ReadStringField(fb, param, MetaParam_Name);
                    if (string.IsNullOrWhiteSpace(paramName))
                    {
                        continue;
                    }

                    bool useNoAnime = ReadBoolField(fb, param, MetaParam_UseNoAnime);
                    if (!useNoAnime)
                    {
                        continue;
                    }

                    int noAnimeField = fb.GetFieldAbsoluteOffset(param, MetaParam_NoAnimeParam);
                    int noAnime = noAnimeField == 0 ? 0 : fb.DerefUOffset(noAnimeField);
                    if (noAnime == 0)
                    {
                        continue;
                    }

                    int variationCount = ReadInt32Field(fb, noAnime, NoAnime_VariationCount);
                    if (variationCount <= 0)
                    {
                        continue;
                    }

                    // Persist selected index for this set/param into OverrideDefaultValue.
                    if (selectionMap.TryGetValue((setName, paramName), out int desiredSelection))
                    {
                        desiredSelection = Math.Clamp(desiredSelection, 0, variationCount - 1);
                        int selField = fb.GetFieldAbsoluteOffset(param, MetaParam_OverrideDefaultValue);
                        if (selField != 0)
                        {
                            fb.WriteInt32(selField, desiredSelection);
                            anyPatched = true;
                        }
                    }

                    // Persist any value overrides by writing the overridden variation values back into the arrays.
                    if (!overrideGroups.TryGetValue((setName, paramName), out var overridesForParam) || overridesForParam.Count == 0)
                    {
                        continue;
                    }

                    int materialListField = fb.GetFieldAbsoluteOffset(noAnime, NoAnime_MaterialList);
                    if (materialListField == 0)
                    {
                        continue;
                    }

                    int matVec = fb.GetVectorDataStartFromUOffsetField(materialListField, out int matCount);
                    if (matVec == 0 || matCount <= 0)
                    {
                        continue;
                    }

                    // Index overrides by material+uniform for quick lookup.
                    var perMaterial = overridesForParam
                        .GroupBy(o => o.MaterialName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

                    for (int m = 0; m < matCount; m++)
                    {
                        int mat = fb.GetVectorElementTableOffset(matVec, m);
                        if (mat == 0)
                        {
                            continue;
                        }

                        string matName = ReadStringField(fb, mat, MetaMaterial_Name);
                        if (string.IsNullOrWhiteSpace(matName) || !perMaterial.TryGetValue(matName, out var overridesForMaterial))
                        {
                            continue;
                        }

                        anyPatched |= PatchFloatList(fb, mat, MetaMaterial_FloatList, overridesForMaterial);
                        anyPatched |= PatchIntList(fb, mat, MetaMaterial_IntList, overridesForMaterial);
                        anyPatched |= PatchVec3List(fb, mat, MetaMaterial_Float3List, overridesForMaterial);
                        anyPatched |= PatchVec4List(fb, mat, MetaMaterial_Float4List, overridesForMaterial);
                    }
                }
            }

            if (safeClone.Length > 0)
            {
                anyPatched |= TryCloneMaterialVariationValuesInPlace(fb, safeClone);
            }

            return anyPatched;
        }

        private static bool TryCloneMaterialVariationValuesInPlace(FlatBufferBinary fb, IReadOnlyList<Model.NewMaterialCloneRequest> cloneRequests)
        {
            int root = fb.GetRootTableOffset();
            int itemListField = fb.GetFieldAbsoluteOffset(root, Root_ItemList);
            if (itemListField == 0)
            {
                return false;
            }

            int itemVec = fb.GetVectorDataStartFromUOffsetField(itemListField, out int itemCount);
            if (itemVec == 0 || itemCount <= 0)
            {
                return false;
            }

            bool any = false;

            for (int itemIndex = 0; itemIndex < itemCount; itemIndex++)
            {
                int item = fb.GetVectorElementTableOffset(itemVec, itemIndex);
                if (item == 0)
                {
                    continue;
                }

                int paramListField = fb.GetFieldAbsoluteOffset(item, Item_ParamList);
                if (paramListField == 0)
                {
                    continue;
                }

                int paramVec = fb.GetVectorDataStartFromUOffsetField(paramListField, out int paramCount);
                if (paramVec == 0 || paramCount <= 0)
                {
                    continue;
                }

                for (int p = 0; p < paramCount; p++)
                {
                    int param = fb.GetVectorElementTableOffset(paramVec, p);
                    if (param == 0)
                    {
                        continue;
                    }

                    bool useNoAnime = ReadBoolField(fb, param, MetaParam_UseNoAnime);
                    if (!useNoAnime)
                    {
                        continue;
                    }

                    int noAnimeField = fb.GetFieldAbsoluteOffset(param, MetaParam_NoAnimeParam);
                    if (noAnimeField == 0)
                    {
                        continue;
                    }

                    int noAnime = fb.DerefUOffset(noAnimeField);
                    if (noAnime == 0)
                    {
                        continue;
                    }

                    int materialListField = fb.GetFieldAbsoluteOffset(noAnime, NoAnime_MaterialList);
                    if (materialListField == 0)
                    {
                        continue;
                    }

                    int matVec = fb.GetVectorDataStartFromUOffsetField(materialListField, out int matCount);
                    if (matVec == 0 || matCount <= 0)
                    {
                        continue;
                    }

                    var nameToMat = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    for (int m = 0; m < matCount; m++)
                    {
                        int mat = fb.GetVectorElementTableOffset(matVec, m);
                        if (mat == 0)
                        {
                            continue;
                        }
                        string matName = ReadStringField(fb, mat, MetaMaterial_Name);
                        if (!string.IsNullOrWhiteSpace(matName) && !nameToMat.ContainsKey(matName))
                        {
                            nameToMat[matName] = mat;
                        }
                    }

                    foreach (var req in cloneRequests)
                    {
                        if (!nameToMat.TryGetValue(req.TemplateName, out int srcMat) ||
                            !nameToMat.TryGetValue(req.NewName, out int dstMat))
                        {
                            continue;
                        }

                        any |= CopyParamTableValues(fb, srcMat, dstMat, MetaMaterial_FloatList, elementSizeBytes: 4);
                        any |= CopyParamTableValues(fb, srcMat, dstMat, MetaMaterial_IntList, elementSizeBytes: 4);
                        any |= CopyParamTableValues(fb, srcMat, dstMat, MetaMaterial_Float3List, elementSizeBytes: 12);
                        any |= CopyParamTableValues(fb, srcMat, dstMat, MetaMaterial_Float4List, elementSizeBytes: 16);
                    }
                }
            }

            return any;
        }

        private static bool CopyParamTableValues(FlatBufferBinary fb, int srcMat, int dstMat, int fieldIndex, int elementSizeBytes)
        {
            int srcField = fb.GetFieldAbsoluteOffset(srcMat, fieldIndex);
            int dstField = fb.GetFieldAbsoluteOffset(dstMat, fieldIndex);
            if (srcField == 0 || dstField == 0)
            {
                return false;
            }

            int srcVec = fb.GetVectorDataStartFromUOffsetField(srcField, out int srcCount);
            int dstVec = fb.GetVectorDataStartFromUOffsetField(dstField, out int dstCount);
            if (srcVec == 0 || dstVec == 0 || srcCount <= 0 || dstCount <= 0)
            {
                return false;
            }

            var srcByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < srcCount; i++)
            {
                int p = fb.GetVectorElementTableOffset(srcVec, i);
                if (p == 0)
                {
                    continue;
                }
                string name = ReadStringField(fb, p, ParamTable_Name);
                if (!string.IsNullOrWhiteSpace(name) && !srcByName.ContainsKey(name))
                {
                    srcByName[name] = p;
                }
            }

            bool any = false;
            for (int i = 0; i < dstCount; i++)
            {
                int dstParam = fb.GetVectorElementTableOffset(dstVec, i);
                if (dstParam == 0)
                {
                    continue;
                }
                string name = ReadStringField(fb, dstParam, ParamTable_Name);
                if (string.IsNullOrWhiteSpace(name) || !srcByName.TryGetValue(name, out int srcParam))
                {
                    continue;
                }

                int srcValuesField = fb.GetFieldAbsoluteOffset(srcParam, ParamTable_Values);
                int dstValuesField = fb.GetFieldAbsoluteOffset(dstParam, ParamTable_Values);
                if (srcValuesField == 0 || dstValuesField == 0)
                {
                    continue;
                }

                int srcDataStart = fb.GetVectorDataStartFromUOffsetField(srcValuesField, out int srcLen);
                int dstDataStart = fb.GetVectorDataStartFromUOffsetField(dstValuesField, out int dstLen);
                if (srcDataStart == 0 || dstDataStart == 0 || srcLen <= 0 || dstLen <= 0)
                {
                    continue;
                }

                int len = Math.Min(srcLen, dstLen);
                int bytes = len * elementSizeBytes;
                Buffer.BlockCopy(fb.Buffer, srcDataStart, fb.Buffer, dstDataStart, bytes);
                any = true;
            }

            return any;
        }

        private static TrmmtMetaMaterial CloneTrmmtMetaMaterial(TrmmtMetaMaterial src, string newName)
        {
            var dst = new TrmmtMetaMaterial
            {
                MaterialName = newName ?? string.Empty,
                FloatParamList = (src.FloatParamList ?? Array.Empty<TrmmtMetaFloatParams>())
                    .Select(p => p == null ? null! : new TrmmtMetaFloatParams { Name = p.Name ?? string.Empty, Values = (float[])(p.Values?.Clone() ?? Array.Empty<float>()) })
                    .Where(p => p != null)
                    .ToArray(),
                Float3ParamList = (src.Float3ParamList ?? Array.Empty<TrmmtMetaFloat3Params>())
                    .Select(p => p == null
                        ? null!
                        : new TrmmtMetaFloat3Params
                        {
                            Name = p.Name ?? string.Empty,
                            Values = (p.Values ?? Array.Empty<Vector3f>()).Select(v => v == null ? null! : new Vector3f { X = v.X, Y = v.Y, Z = v.Z }).ToArray()
                        })
                    .Where(p => p != null)
                    .ToArray(),
                Float4ParamList = (src.Float4ParamList ?? Array.Empty<TrmmtMetaFloat4Params>())
                    .Select(p => p == null
                        ? null!
                        : new TrmmtMetaFloat4Params
                        {
                            Name = p.Name ?? string.Empty,
                            Values = (p.Values ?? Array.Empty<Vector4f>()).Select(v => v == null ? null! : new Vector4f { W = v.W, X = v.X, Y = v.Y, Z = v.Z }).ToArray()
                        })
                    .Where(p => p != null)
                    .ToArray(),
                IntParamList = (src.IntParamList ?? Array.Empty<TrmmtMetaIntParams>())
                    .Select(p => p == null ? null! : new TrmmtMetaIntParams { Name = p.Name ?? string.Empty, Values = (int[])(p.Values?.Clone() ?? Array.Empty<int>()) })
                    .Where(p => p != null)
                    .ToArray()
            };

            return dst;
        }

        private static bool PatchFloatList(FlatBufferBinary fb, int mat, int fieldIndex, List<Model.MaterialMetadataValueOverride> overrides)
        {
            int listField = fb.GetFieldAbsoluteOffset(mat, fieldIndex);
            if (listField == 0)
            {
                return false;
            }

            int vec = fb.GetVectorDataStartFromUOffsetField(listField, out int count);
            if (vec == 0 || count <= 0)
            {
                return false;
            }

            bool patched = false;
            for (int i = 0; i < count; i++)
            {
                int p = fb.GetVectorElementTableOffset(vec, i);
                if (p == 0)
                {
                    continue;
                }

                string name = ReadStringField(fb, p, ParamTable_Name);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                foreach (var ov in overrides.Where(o => string.Equals(o.UniformName, name, StringComparison.OrdinalIgnoreCase) && o.Value is float or int))
                {
                    int valuesField = fb.GetFieldAbsoluteOffset(p, ParamTable_Values);
                    if (valuesField == 0)
                    {
                        continue;
                    }

                    int dataStart = fb.GetVectorDataStartFromUOffsetField(valuesField, out int len);
                    if (dataStart == 0 || len <= 0)
                    {
                        continue;
                    }

                    int idx = Math.Clamp(ov.VariationIndex, 0, len - 1);
                    float value = ov.Value is int ni ? ni : (float)ov.Value;
                    fb.WriteSingle(dataStart + (idx * 4), value);
                    patched = true;
                }
            }

            return patched;
        }

        private static bool PatchIntList(FlatBufferBinary fb, int mat, int fieldIndex, List<Model.MaterialMetadataValueOverride> overrides)
        {
            int listField = fb.GetFieldAbsoluteOffset(mat, fieldIndex);
            if (listField == 0)
            {
                return false;
            }

            int vec = fb.GetVectorDataStartFromUOffsetField(listField, out int count);
            if (vec == 0 || count <= 0)
            {
                return false;
            }

            bool patched = false;
            for (int i = 0; i < count; i++)
            {
                int p = fb.GetVectorElementTableOffset(vec, i);
                if (p == 0)
                {
                    continue;
                }

                string name = ReadStringField(fb, p, ParamTable_Name);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                foreach (var ov in overrides.Where(o => string.Equals(o.UniformName, name, StringComparison.OrdinalIgnoreCase) && o.Value is int or float))
                {
                    int valuesField = fb.GetFieldAbsoluteOffset(p, ParamTable_Values);
                    if (valuesField == 0)
                    {
                        continue;
                    }

                    int dataStart = fb.GetVectorDataStartFromUOffsetField(valuesField, out int len);
                    if (dataStart == 0 || len <= 0)
                    {
                        continue;
                    }

                    int idx = Math.Clamp(ov.VariationIndex, 0, len - 1);
                    int value = ov.Value is float nf ? (int)MathF.Round(nf) : (int)ov.Value;
                    fb.WriteInt32(dataStart + (idx * 4), value);
                    patched = true;
                }
            }

            return patched;
        }

        private static bool PatchVec3List(FlatBufferBinary fb, int mat, int fieldIndex, List<Model.MaterialMetadataValueOverride> overrides)
        {
            int listField = fb.GetFieldAbsoluteOffset(mat, fieldIndex);
            if (listField == 0)
            {
                return false;
            }

            int vec = fb.GetVectorDataStartFromUOffsetField(listField, out int count);
            if (vec == 0 || count <= 0)
            {
                return false;
            }

            bool patched = false;
            for (int i = 0; i < count; i++)
            {
                int p = fb.GetVectorElementTableOffset(vec, i);
                if (p == 0)
                {
                    continue;
                }

                string name = ReadStringField(fb, p, ParamTable_Name);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                foreach (var ov in overrides.Where(o => string.Equals(o.UniformName, name, StringComparison.OrdinalIgnoreCase) && o.Value is Vector3))
                {
                    int valuesField = fb.GetFieldAbsoluteOffset(p, ParamTable_Values);
                    if (valuesField == 0)
                    {
                        continue;
                    }

                    int dataStart = fb.GetVectorDataStartFromUOffsetField(valuesField, out int len);
                    if (dataStart == 0 || len <= 0)
                    {
                        continue;
                    }

                    int idx = Math.Clamp(ov.VariationIndex, 0, len - 1);
                    int elem = dataStart + (idx * 12);
                    var v = (Vector3)ov.Value;
                    fb.WriteSingle(elem + 0, v.X);
                    fb.WriteSingle(elem + 4, v.Y);
                    fb.WriteSingle(elem + 8, v.Z);
                    patched = true;
                }
            }

            return patched;
        }

        private static bool PatchVec4List(FlatBufferBinary fb, int mat, int fieldIndex, List<Model.MaterialMetadataValueOverride> overrides)
        {
            int listField = fb.GetFieldAbsoluteOffset(mat, fieldIndex);
            if (listField == 0)
            {
                return false;
            }

            int vec = fb.GetVectorDataStartFromUOffsetField(listField, out int count);
            if (vec == 0 || count <= 0)
            {
                return false;
            }

            bool patched = false;
            for (int i = 0; i < count; i++)
            {
                int p = fb.GetVectorElementTableOffset(vec, i);
                if (p == 0)
                {
                    continue;
                }

                string name = ReadStringField(fb, p, ParamTable_Name);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                foreach (var ov in overrides.Where(o => string.Equals(o.UniformName, name, StringComparison.OrdinalIgnoreCase) && o.Value is Vector4))
                {
                    int valuesField = fb.GetFieldAbsoluteOffset(p, ParamTable_Values);
                    if (valuesField == 0)
                    {
                        continue;
                    }

                    int dataStart = fb.GetVectorDataStartFromUOffsetField(valuesField, out int len);
                    if (dataStart == 0 || len <= 0)
                    {
                        continue;
                    }

                    int idx = Math.Clamp(ov.VariationIndex, 0, len - 1);
                    int elem = dataStart + (idx * 16);
                    var v = (Vector4)ov.Value;
                    // Vector4f struct is W,X,Y,Z on disk; we store values in OpenTK as X,Y,Z,W.
                    fb.WriteSingle(elem + 0, v.X);
                    fb.WriteSingle(elem + 4, v.Y);
                    fb.WriteSingle(elem + 8, v.Z);
                    fb.WriteSingle(elem + 12, v.W);
                    patched = true;
                }
            }

            return patched;
        }

        private static Dictionary<(string SetName, string ParamName), int> BuildSelectionMap(KeyValuePair<string, int>[] selections)
        {
            var map = new Dictionary<(string, string), int>();
            if (selections == null || selections.Length == 0)
            {
                return map;
            }

            foreach (var kv in selections)
            {
                if (TryParseSelectionKey(kv.Key, out var setName, out var paramName))
                {
                    map[(NormalizeSetName(setName), paramName)] = kv.Value;
                }
            }

            return map;
        }

        private static Dictionary<(string SetName, string ParamName), List<Model.MaterialMetadataValueOverride>> BuildOverrideGroups(Model.MaterialMetadataValueOverride[] overrides)
        {
            var map = new Dictionary<(string, string), List<Model.MaterialMetadataValueOverride>>();
            if (overrides == null || overrides.Length == 0)
            {
                return map;
            }

            foreach (var ov in overrides)
            {
                if (string.IsNullOrWhiteSpace(ov.SetName) || string.IsNullOrWhiteSpace(ov.MetadataParamName))
                {
                    continue;
                }

                var key = (NormalizeSetName(ov.SetName), ov.MetadataParamName);
                if (!map.TryGetValue(key, out var list))
                {
                    list = new List<Model.MaterialMetadataValueOverride>();
                    map[key] = list;
                }
                list.Add(ov);
            }

            return map;
        }

        private static bool TryParseSelectionKey(string key, out string setName, out string paramName)
        {
            setName = string.Empty;
            paramName = string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            int sep = key.IndexOf("::", StringComparison.Ordinal);
            if (sep <= 0 || sep + 2 >= key.Length)
            {
                return false;
            }

            setName = key.Substring(0, sep);
            paramName = key.Substring(sep + 2);
            return !string.IsNullOrWhiteSpace(setName) && !string.IsNullOrWhiteSpace(paramName);
        }

        private static string ReadSetName(FlatBufferBinary fb, int itemTable, int itemIndex)
        {
            string name = ReadStringField(fb, itemTable, Item_Name);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return itemIndex == 0 ? "default" : $"set_{itemIndex}";
        }

        private static string NormalizeSetName(string setName)
        {
            if (string.IsNullOrWhiteSpace(setName))
            {
                return "default";
            }

            // Older UI state used "<default>" when no set name exists.
            if (string.Equals(setName, "<default>", StringComparison.OrdinalIgnoreCase))
            {
                return "default";
            }

            return setName;
        }

        private static string ReadStringField(FlatBufferBinary fb, int table, int fieldIndex)
        {
            int field = fb.GetFieldAbsoluteOffset(table, fieldIndex);
            return field == 0 ? string.Empty : fb.ReadStringAtUOffsetField(field);
        }

        private static bool ReadBoolField(FlatBufferBinary fb, int table, int fieldIndex)
        {
            int field = fb.GetFieldAbsoluteOffset(table, fieldIndex);
            return field != 0 && fb.ReadBool(field);
        }

        private static int ReadInt32Field(FlatBufferBinary fb, int table, int fieldIndex)
        {
            int field = fb.GetFieldAbsoluteOffset(table, fieldIndex);
            return field == 0 ? 0 : fb.ReadInt32(field);
        }
    }
}
