using System.Collections.Concurrent;
using GFTool.Renderer.Scene;
using GFTool.Renderer.Scene.GraphicsObjects;

namespace GFTool.Renderer
{
    public partial class RenderContext
    {
        private readonly ConcurrentQueue<RefObject> pendingSceneObjectAdds = new();
        private readonly ConcurrentQueue<RefObject> pendingSceneObjectRemoves = new();

        public void AddSceneObject(RefObject obj)
        {
            if (obj == null)
            {
                return;
            }

            pendingSceneObjectAdds.Enqueue(obj);
        }

        public void RemoveSceneObject(RefObject obj)
        {
            if (obj == null)
            {
                return;
            }

            pendingSceneObjectRemoves.Enqueue(obj);
        }

        private void ProcessPendingSceneObjects()
        {
            var root = SceneGraph.Instance.GetRoot();

            while (pendingSceneObjectRemoves.TryDequeue(out var rem))
            {
                try
                {
                    root.children.Remove(rem);
                }
                catch
                {
                    // ignore
                }
            }

            while (pendingSceneObjectAdds.TryDequeue(out var add))
            {
                try
                {
                    if (!root.children.Contains(add))
                    {
                        root.children.Add(add);
                        add.Setup();
                    }
                }
                catch
                {
                    // ignore
                }
            }
        }
    }
}
