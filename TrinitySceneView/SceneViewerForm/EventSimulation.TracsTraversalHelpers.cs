using System;
using System.Collections.Generic;

namespace TrinitySceneView
{
    public partial class SceneViewerForm
    {
        private static string? FindClosestEntryDescendant(MotionAnimDb db, string stateName, HashSet<string> visited)
        {
            if (string.IsNullOrWhiteSpace(stateName))
            {
                return null;
            }

            string prefix = stateName.EndsWith("/", StringComparison.Ordinal) ? stateName : stateName + "/";
            int baseDepth = CountSegments(stateName);

            string? best = null;
            int bestDepth = int.MaxValue;

            foreach (var st in db.States.Values)
            {
                if (!string.Equals(st.Type, "Entry", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (string.IsNullOrWhiteSpace(st.Name) || !st.Name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }
                if (visited.Contains(st.Name))
                {
                    continue;
                }

                int depth = CountSegments(st.Name);
                if (depth <= baseDepth)
                {
                    continue;
                }

                if (depth < bestDepth)
                {
                    best = st.Name;
                    bestDepth = depth;
                }
                else if (depth == bestDepth && best != null && string.Compare(st.Name, best, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    best = st.Name;
                }
            }

            return best;
        }

        private static int CountSegments(string name)
        {
            int count = 1;
            for (int i = 0; i < name.Length; i++)
            {
                if (name[i] == '/')
                {
                    count++;
                }
            }
            return count;
        }
    }
}
