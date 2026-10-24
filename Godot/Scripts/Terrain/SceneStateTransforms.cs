using System;
using Godot;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
/// Node transforms read straight off a PackedScene state. Instantiating a scene leaves
/// GlobalTransform at identity outside the tree, and a headless bake has no tree to attach to
/// </summary>
public static class SceneStateTransforms
{
    /// <summary>
    /// Node transform composed with every ancestor in the scene, i.e. the transform the node gets
    /// when the scene root sits at <paramref name="sceneRootParent"/>
    /// </summary>
    public static bool TryGetGlobal (
        PackedScene scene,
        string nodeTypeName,
        string nodeName,
        Transform3D sceneRootParent,
        out Transform3D transform)
    {
        transform = Transform3D.Identity;
        var state = scene.GetState ();
        var index = FindNode (state, nodeTypeName, nodeName);
        if (index < 0)
        {
            return false;
        }

        var local = GetNodeTransform (state, index);
        var parentIndex = GetParentIndex (state, index);
        var composed = local;
        while (parentIndex >= 0)
        {
            composed = GetNodeTransform (state, parentIndex) * composed;
            parentIndex = GetParentIndex (state, parentIndex);
        }

        transform = sceneRootParent * composed;
        return true;
    }

    /// <summary>
    /// Node in <paramref name="scene"/> that instances <paramref name="instancedScenePath"/>,
    /// and its transform including scene ancestors. MainServer's TerrainScene node is the
    /// placement parent for everything inside terrain_scene
    /// </summary>
    public static bool TryGetInstance (
        PackedScene scene,
        string instancedScenePath,
        out Transform3D transform)
    {
        transform = Transform3D.Identity;
        var state = scene.GetState ();
        for (var i = 0; i < state.GetNodeCount (); i++)
        {
            if (!SameScene (state.GetNodeInstance (i), instancedScenePath))
            {
                continue;
            }

            var composed = GetNodeTransform (state, i);
            var parentIndex = GetParentIndex (state, i);
            while (parentIndex >= 0)
            {
                composed = GetNodeTransform (state, parentIndex) * composed;
                parentIndex = GetParentIndex (state, parentIndex);
            }

            transform = composed;
            return true;
        }

        return false;
    }

    private static bool SameScene (PackedScene? instance, string path)
    {
        if (instance is null)
        {
            return false;
        }

        var left = (instance.ResourcePath ?? string.Empty).Replace ('\\', '/');
        var right = path.Replace ('\\', '/');
        return string.Equals (left, right, StringComparison.Ordinal)
               || string.Equals (left.TrimStart ('*'), right, StringComparison.Ordinal);
    }

    private static int FindNode (SceneState state, string nodeTypeName, string nodeName)
    {
        for (var i = 0; i < state.GetNodeCount (); i++)
        {
            if (state.GetNodeName (i).ToString () != nodeName)
            {
                continue;
            }

            if (nodeTypeName.Length == 0 || state.GetNodeType (i).ToString () == nodeTypeName)
            {
                return i;
            }
        }

        return -1;
    }

    private static int GetParentIndex (SceneState state, int index) => state.GetNodeIndex (index);

    private static Transform3D GetNodeTransform (SceneState state, int index)
    {
        for (var p = 0; p < state.GetNodePropertyCount (index); p++)
        {
            if (state.GetNodePropertyName (index, p).ToString () != "transform")
            {
                continue;
            }

            var value = state.GetNodePropertyValue (index, p);
            if (value.VariantType == Variant.Type.Transform3D)
            {
                return value.AsTransform3D ();
            }

            return Transform3D.Identity;
        }

        return Transform3D.Identity;
    }
}
