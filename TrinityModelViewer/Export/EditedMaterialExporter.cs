using System;
using System.IO;

namespace TrinityModelViewer.Export
{
    internal static class EditedMaterialExporter
    {
        public static void ExportEditedTrmtr(string sourceTrmtrPath, GFTool.Renderer.Scene.GraphicsObjects.Model model, string outputTrmtrPath)
        {
            if (string.IsNullOrWhiteSpace(sourceTrmtrPath)) throw new ArgumentException("Missing source TRMTR path.", nameof(sourceTrmtrPath));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (string.IsNullOrWhiteSpace(outputTrmtrPath)) throw new ArgumentException("Missing output TRMTR path.", nameof(outputTrmtrPath));
            if (!File.Exists(sourceTrmtrPath)) throw new FileNotFoundException("Source TRMTR not found.", sourceTrmtrPath);

            // Reserialize from the FlatSharp model so runtime edits beyond uniform overrides (e.g. sampler wrap)
            // can be exported. This still preserves all fields that are represented in our TrmtrFile schema.
            TrmtrReserializePatcher.ExportEditedTrmtrByReserialize(sourceTrmtrPath, model, outputTrmtrPath);
        }
    }
}
