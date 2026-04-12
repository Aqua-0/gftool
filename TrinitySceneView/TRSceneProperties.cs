using GFTool.Core.Flatbuffers.TR.Scene.Components;
using System;
using System.Linq;
using System.Text;

namespace TrinitySceneView
{
    public static class TRSceneProperties
    {
        private static string CameraEntity(object objData)
        {
            StringBuilder sb = new StringBuilder();

            var data = (trinity_CameraEntity)objData;
            sb.AppendLine("Name: " + data.Name);
            sb.AppendLine("Target: " + data.TargetName);

            return sb.ToString();
        }

        private static string SceneObject(object objData)
        {
            StringBuilder sb = new StringBuilder();

            var data = (trinity_SceneObject)objData;
            sb.AppendLine("Name: " + (data.Name ?? string.Empty));
            if (!string.IsNullOrEmpty(data.AttachJointName))
                sb.AppendLine("Attach joint name: " + data.AttachJointName);

            var tags = data.TagList ?? Array.Empty<string>();
            sb.AppendLine(string.Format("Tags: ({0})", tags.Length));

            foreach (var tag in tags)
            {
                sb.AppendLine(string.Format("  {0}" + Environment.NewLine, tag == string.Empty ? "(Blank)" : tag));
            }

            var layers = data.Layers ?? Array.Empty<ObjectLayer>();
            if (layers.Length > 0)
            {
                sb.AppendLine(string.Format("Layers: ({0})", layers.Length));
                foreach (var layer in layers)
                {
                    sb.AppendLine(string.Format("  {0}" + Environment.NewLine, layer?.Name ?? string.Empty));
                }
            }

            return sb.ToString();
        }

        private static string OverrideSensorData(object objData)
        {
            StringBuilder sb = new StringBuilder();

            var data = (trinity_OverrideSensorData)objData;
            sb.AppendLine("Realizing Dist: " + data.RealizingDistance);
            sb.AppendLine("Unrealizing Dist: " + data.UnrealizingDistance);
            sb.AppendLine("Loading Dist: " + data.LoadingDistance);
            sb.AppendLine("Unloading Dist: " + data.UnloadingDistance);

            return sb.ToString();
        }

        private static string ScriptComponent(object objData)
        {
            StringBuilder sb = new StringBuilder();

            var data = (trinity_ScriptComponent)objData;
            sb.AppendLine("File: " + data.FilePath);
            sb.AppendLine("Package: " + data.PackageName);
            sb.AppendLine("Is static: " + (data.IsStatic ? "True" : "False"));

            return sb.ToString();
        }

        private static string TextComponent(object objData)
        {
            StringBuilder sb = new StringBuilder();

            var data = (pe_TextComponent)objData;
            sb.AppendLine("File: " + data.FilePath);

            return sb.ToString();
        }

        private static string InputEventTriggerComponent(object objData)
        {
            StringBuilder sb = new StringBuilder();

            var data = (pe_InputEventTriggerComponent)objData;
            sb.AppendLine("Input name: " + data.InputName);
            sb.AppendLine("Resource name: " + data.ResourceName);

            return sb.ToString();
        }

        private static string PropertySheet(object objData)
        {
            var sb = new StringBuilder();
            var data = (trinity_PropertySheet)objData;

            sb.AppendLine("Name: " + (data.name ?? string.Empty));
            sb.AppendLine("Template: " + (data.template ?? string.Empty));

            try
            {
                var entries = data.entries ?? Array.Empty<PropertySheetEntry>();
                sb.AppendLine($"Entries: ({entries.Length})");

                int entryIndex = 0;
                foreach (var entry in entries)
                {
                    entryIndex++;
                    if (entry?.properties == null || entry.properties.Length == 0)
                    {
                        continue;
                    }

                    sb.AppendLine($" Entry {entryIndex}: properties ({entry.properties.Length})");
                    foreach (var prop in entry.properties)
                    {
                        if (prop == null) continue;
                        sb.AppendLine($"  {prop.name} = {(prop.value ? "true" : "false")}");
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"(Failed to enumerate entries: {ex.GetType().Name}: {ex.Message})");
            }

            return sb.ToString();
        }

        private static string PlacementRegistry(object objData)
        {
            var sb = new StringBuilder();
            var data = (trinity_PlacementRegistry)objData;

            sb.AppendLine("Type: trinity_PlacementRegistry");
            if (data.Entry.Discriminator == 0)
            {
                sb.AppendLine("Item: (null)");
                return sb.ToString();
            }

            sb.AppendLine("EntryDiscriminator: " + data.Entry.Discriminator);
            data.Entry.Switch(
                defaultCase: () => sb.AppendLine("Entry: (unhandled)"),
                case1: (PlacementObjectArray ol) =>
                {
                    sb.AppendLine("EntryType: PlacementObjectArray");
                    sb.AppendLine("Objects: " + (ol.Table?.Count ?? 0));
                    if (ol.Table != null)
                    {
                        foreach (var o in ol.Table.Take(10))
                        {
                            sb.AppendLine($"  Name={o?.Name} Type={o?.Type} File={o?.File}");
                        }
                    }
                },
                case2: (PlacementObjectTemplateArray tl) =>
                {
                    sb.AppendLine("EntryType: PlacementObjectTemplateArray");
                    sb.AppendLine("ObjectTemplates: " + (tl.Table?.Count ?? 0));
                    if (tl.Table != null)
                    {
                        foreach (var t in tl.Table.Take(10))
                        {
                            sb.AppendLine($"  Name={t?.Name} Path={t?.Path}");
                        }
                    }
                },
                case3: (PlacementPositionArray pl) =>
                {
                    sb.AppendLine("EntryType: PlacementPositionArray");
                    sb.AppendLine("Positions: " + (pl.Table?.Count ?? 0));
                    if (pl.Table != null)
                    {
                        foreach (var p in pl.Table.Take(10))
                        {
                            sb.AppendLine($"  Name={p?.Name} pos=({p?.Position?.X},{p?.Position?.Y},{p?.Position?.Z}) rot=({p?.Rotation?.X},{p?.Rotation?.Y},{p?.Rotation?.Z})");
                        }
                    }
                },
                case4: (PlacementSpawnerArray sl) =>
                {
                    sb.AppendLine("EntryType: PlacementSpawnerArray");
                    sb.AppendLine("Spawners: " + (sl.Table?.Count ?? 0));
                    if (sl.Table != null)
                    {
                        foreach (var s in sl.Table.Take(10))
                        {
                            sb.AppendLine($"  Name={s?.Name} Scene={s?.Scene} args={s?.Arguments?.Count ?? 0}");
                        }
                    }
                });

            return sb.ToString();
        }

        public static string GetProperties(string sceneComponent, object objData)
        {
            string ret = string.Empty;

            switch (sceneComponent)
            {
                case "trinity_CameraEntity": ret = CameraEntity(objData); break;
                case "trinity_SceneObject": ret = SceneObject(objData); break;
                case "trinity_OverrideSensorData": ret = OverrideSensorData(objData); break;
                case "trinity_ScriptComponent": ret = ScriptComponent(objData); break;
                case "pe_TextComponent": ret = TextComponent(objData); break;
                case "pe_InputEventTriggerComponent": ret = InputEventTriggerComponent(objData); break;
                case "trinity_PropertySheet": ret = PropertySheet(objData); break;
                case "trinity_PlacementRegistry": ret = PlacementRegistry(objData); break;

            }

            return ret;
        }
    }
}
