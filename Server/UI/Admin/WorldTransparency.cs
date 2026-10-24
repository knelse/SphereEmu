using System.Collections.Generic;
using Godot;

namespace SphServer.Server.UI.Admin;

/// <summary>
/// Draws the server world at a given alpha, so collider overlays read through walls. Materials are
/// shared resources, so one write per material covers every instance. Originals restore on close.
/// A node needs a pass only when it is new or gained children, which streaming does, so visited
/// subtrees are skipped and the preview's own overlay root is never walked
/// </summary>
public static class WorldTransparency
{
    private static readonly Dictionary<StandardMaterial3D, (BaseMaterial3D.TransparencyEnum Transparency, Color Albedo)>
        Originals = new ();

    // Node -> its child count when last walked, so an unchanged, already-drawn subtree is skipped
    private static readonly Dictionary<Node, int> Visited = new ();
    private static int visitedPruneCount;

    public static void Apply (Node root, float alpha, Node? skipSubtreeRoot = null)
    {
        var seen = new HashSet<StandardMaterial3D> ();
        foreach (var material in Collect (root, skipSubtreeRoot, seen))
        {
            if (!Originals.ContainsKey (material))
            {
                Originals[material] = (material.Transparency, material.AlbedoColor);
            }

            material.Transparency = alpha >= 0.999f
                ? BaseMaterial3D.TransparencyEnum.Disabled
                : BaseMaterial3D.TransparencyEnum.Alpha;
            var albedo = material.AlbedoColor;
            albedo.A = alpha;
            material.AlbedoColor = albedo;
        }
    }

    public static void Restore ()
    {
        foreach (var (material, saved) in Originals)
        {
            if (!GodotObject.IsInstanceValid (material))
            {
                continue;
            }

            material.Transparency = saved.Transparency;
            material.AlbedoColor = saved.Albedo;
        }

        Originals.Clear ();
        Visited.Clear ();
    }

    private static IEnumerable<StandardMaterial3D> Collect (Node root, Node? skipSubtreeRoot, HashSet<StandardMaterial3D> seen)
    {
        PruneFreed ();
        var stack = new Stack<Node> ();
        PushChildren (root, stack, skipSubtreeRoot);
        while (stack.Count > 0)
        {
            var node = stack.Pop ();
            var children = node.GetChildCount ();
            if (Visited.TryGetValue (node, out var visitedCount) && visitedCount == children)
            {
                // Streaming never edits a cell node in place, it adds new ones
                continue;
            }

            Visited[node] = children;
            foreach (var material in NodeMaterials (node, seen))
            {
                yield return material;
            }

            PushChildren (node, stack, skipSubtreeRoot);
        }
    }

    private static void PushChildren (Node node, Stack<Node> stack, Node? skipSubtreeRoot)
    {
        foreach (var child in node.GetChildren ())
        {
            if (!GodotObject.IsInstanceValid (child) || ReferenceEquals (child, skipSubtreeRoot))
            {
                continue;
            }

            stack.Push (child);
        }
    }

    private static void PruneFreed ()
    {
        // A streamed cell node frees constantly, so a sweep over the keys every pass is the walk's
        // own cost. The sweep is rare, and a stale key only means one extra pass on a live node
        if (++visitedPruneCount < 64)
        {
            return;
        }

        visitedPruneCount = 0;
        List<Node>? dead = null;
        foreach (var node in Visited.Keys)
        {
            if (!GodotObject.IsInstanceValid (node))
            {
                dead ??= [];
                dead.Add (node);
            }
        }

        if (dead is null)
        {
            return;
        }

        foreach (var node in dead)
        {
            Visited.Remove (node);
        }
    }

    private static IEnumerable<StandardMaterial3D> NodeMaterials (Node node, HashSet<StandardMaterial3D> seen)
    {
        // Terrain, placements and monsters reach their materials through these three node types
        switch (node)
        {
            case MeshInstance3D meshNode:
                foreach (var material in Surfaces (meshNode.Mesh, seen))
                {
                    yield return material;
                }

                break;
            case MultiMeshInstance3D multiMeshNode:
                foreach (var material in Surfaces (multiMeshNode.Multimesh?.Mesh, seen))
                {
                    yield return material;
                }

                break;
            case GridMap { MeshLibrary: { } library }:
                foreach (var itemId in library.GetItemList ())
                {
                    foreach (var material in Surfaces (library.GetItemMesh (itemId), seen))
                    {
                        yield return material;
                    }
                }

                break;
        }
    }

    private static IEnumerable<StandardMaterial3D> Surfaces (Mesh? mesh, HashSet<StandardMaterial3D> seen)
    {
        if (mesh is null)
        {
            yield break;
        }

        for (var index = 0; index < mesh.GetSurfaceCount (); index++)
        {
            if (mesh.SurfaceGetMaterial (index) is StandardMaterial3D material && seen.Add (material))
            {
                yield return material;
            }
        }
    }
}
