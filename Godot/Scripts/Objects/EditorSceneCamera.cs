#if TOOLS
using Godot;
using SphServer.Helpers;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects;

/// <summary>
/// Moves the Godot 3D editor viewport camera to look at a world-space point (editor only).
/// </summary>
public static class EditorSceneCamera
{
	public static void JumpToWorldPosition(Vector3 worldPosition)
	{
		if (!Engine.IsEditorHint())
		{
			return;
		}

		var editorInterface = EditorInterface.Singleton;
		editorInterface.SetMainScreenEditor("3D");

		var viewport = editorInterface.GetEditorViewport3D();
		if (viewport == null)
		{
			GD.PushWarning("EditorSceneCamera: 3D viewport not available.");
			return;
		}

		var camera = viewport.GetCamera3D();
		if (camera == null)
		{
			GD.PushWarning("EditorSceneCamera: 3D editor camera not available.");
			return;
		}

		const float distance = 20f;
		var eye = worldPosition + new Vector3(distance, distance * 0.7f, distance);
		camera.GlobalPosition = eye;
		camera.LookAt(worldPosition, Vector3.Up);
	}

	public static void JumpToCastleTablet(Node context, Castles castle)
	{
		if (!Engine.IsEditorHint())
		{
			return;
		}

		var tablet = FindCastleTabletInEditedScene(context, castle);
		if (tablet is null)
		{
			GD.PushWarning($"EditorSceneCamera: no CastleTablet found for {castle}.");
			return;
		}

		JumpToWorldPosition(tablet.GlobalPosition);
	}

	private static CastleTablet? FindCastleTabletInEditedScene(Node context, Castles castle)
	{
		var root = context.GetTree()?.EditedSceneRoot;
		if (root is null)
		{
			return null;
		}

		foreach (var node in root.FindChildren("*", recursive: true))
		{
			if (node is CastleTablet tablet && tablet.Castle == castle)
			{
				return tablet;
			}
		}

		return null;
	}
}
#else
using Godot;
using SphServer.Helpers;

namespace SphServer.Godot.Scripts.Objects;

/// <summary>Export/headless stub — editor camera helpers are unavailable outside TOOLS builds.</summary>
public static class EditorSceneCamera
{
	public static void JumpToWorldPosition(Vector3 worldPosition)
	{
	}

	public static void JumpToCastleTablet(Node context, Castles castle)
	{
	}
}
#endif
