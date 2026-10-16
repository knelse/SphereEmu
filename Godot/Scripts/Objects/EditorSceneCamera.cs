#if TOOLS
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Godot;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects;

public static class EditorSceneCamera
{
    public static void JumpToWorldPosition (Vector3 worldPosition)
    {
        if (!Engine.IsEditorHint ())
        {
            return;
        }

        var editorInterface = EditorInterface.Singleton;
        editorInterface.SetMainScreenEditor ("3D");

        var viewport = editorInterface.GetEditorViewport3D ();
        if (viewport == null)
        {
            GD.PushWarning ("EditorSceneCamera: 3D viewport not available.");
            return;
        }

        var camera = viewport.GetCamera3D ();
        if (camera == null)
        {
            GD.PushWarning ("EditorSceneCamera: 3D editor camera not available.");
            return;
        }

        const float distance = 20f;
        var eye = worldPosition + new Vector3 (distance, distance * 0.7f, distance);
        camera.GlobalPosition = eye;
        camera.LookAt (worldPosition, Vector3.Up);
    }

    /// <summary>
    /// Loaded node first, then the chunk scene that stores this guid
    /// </summary>
    public static bool TryJumpToEntity (Node context, Guid entityId)
    {
        if (!Engine.IsEditorHint ())
        {
            return false;
        }

        if (!TryFindInEditedScene (context, entityId, out var position)
            && !TryFindInChunkFiles (entityId, out position))
        {
            GD.PushWarning ($"EditorSceneCamera: no object {entityId}.");
            return false;
        }

        JumpToWorldPosition (position);
        return true;
    }

    private static bool TryFindInEditedScene (Node context, Guid entityId, out Vector3 position)
    {
        position = default;
        var root = context.GetTree ()?.EditedSceneRoot;
        if (root is null)
        {
            return false;
        }

        foreach (var node in root.FindChildren ("*", recursive: true))
        {
            if (node is WorldObject world && world.EntityID == entityId)
            {
                position = world.GlobalPosition;
                return true;
            }
        }

        return false;
    }

    private static Dictionary<Guid, Vector3>? positionsByEntityId;

    private static bool TryFindInChunkFiles (Guid entityId, out Vector3 position)
    {
        positionsByEntityId ??= LoadChunkPositions ();
        return positionsByEntityId.TryGetValue (entityId, out position);
    }

    private static Dictionary<Guid, Vector3> LoadChunkPositions ()
    {
        var positions = new Dictionary<Guid, Vector3> ();
        var chunks = ProjectSettings.GlobalizePath ("res://Godot/World/Chunks");
        if (!Directory.Exists (chunks))
        {
            return positions;
        }

        var transform = new Regex (@"transform = Transform3D\(([^)]+)\)", RegexOptions.CultureInvariant);
        var entityId = new Regex ("EntityIdString = \"([^\"]+)\"", RegexOptions.CultureInvariant);
        foreach (var path in Directory.EnumerateFiles (chunks, "*.tscn", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText (path);
            foreach (var block in text.Split ("\n[node "))
            {
                var transformMatch = transform.Match (block);
                var idMatch = entityId.Match (block);
                if (!transformMatch.Success || !idMatch.Success
                    || !Guid.TryParse (idMatch.Groups[1].Value, out var id))
                {
                    continue;
                }

                var parts = transformMatch.Groups[1].Value.Split (',');
                if (parts.Length < 3)
                {
                    continue;
                }

                var origin = parts.Length - 3;
                if (!float.TryParse (parts[origin], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                    || !float.TryParse (parts[origin + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                    || !float.TryParse (parts[origin + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                {
                    continue;
                }

                positions[id] = new Vector3 (x, y, z);
            }
        }

        return positions;
    }
}
#else
using Godot;

namespace SphServer.Godot.Scripts.Objects;

/// <summary>
/// Export/headless stub. Editor camera helpers are unavailable outside TOOLS builds
/// </summary>
public static class EditorSceneCamera
{
	public static void JumpToWorldPosition (Vector3 worldPosition)
	{
	}

	public static bool TryJumpToEntity (Node context, Guid entityId) => false;
}
#endif
