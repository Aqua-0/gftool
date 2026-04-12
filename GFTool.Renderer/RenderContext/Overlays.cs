using GFTool.Renderer.Core.Graphics;
using GFTool.Renderer.Scene;
using GFTool.Renderer.Scene.GraphicsObjects;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace GFTool.Renderer
{
    public partial class RenderContext
    {
        public Task ReplaceHeightFieldOverlayAsync(HeightFieldMesh? mesh)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            EnqueueGlWork(new ReplaceHeightFieldOverlayWorkItem(mesh, tcs));
            return tcs.Task;
        }

        private sealed class ReplaceHeightFieldOverlayWorkItem : IGlWorkItem
        {
            private readonly HeightFieldMesh? mesh;
            private readonly TaskCompletionSource tcs;
            private bool done;

            public ReplaceHeightFieldOverlayWorkItem(HeightFieldMesh? mesh, TaskCompletionSource tcs)
            {
                this.mesh = mesh;
                this.tcs = tcs;
            }

            public bool Step()
            {
                if (done)
                {
                    return true;
                }

                try
                {
                    var root = SceneGraph.Instance.GetRoot();
                    var existing = root.children.OfType<HeightFieldMesh>().ToList();
                    foreach (var ex in existing)
                    {
                        try { ex.Dispose(); } catch { }
                        root.children.Remove(ex);
                    }

                    if (mesh != null)
                    {
                        root.children.Add(mesh);
                        mesh.Setup();
                    }

                    tcs.TrySetResult();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                finally
                {
                    done = true;
                }

                return true;
            }
        }
    }
}
