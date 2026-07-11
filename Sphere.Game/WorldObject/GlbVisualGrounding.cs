using Godot;

namespace SphServer.Sphere.Game.WorldObject;

/// <summary>
///     Shared GLB mesh bounds and feet-on-ground offset used by <see cref="WorldObject" /> and
///     <see cref="MonsterMultiMeshVisuals" />.
/// </summary>
internal static partial class GlbVisualGrounding
{
	public const string DefaultModelsDirectory = "res://Godot/Models/";

	private static readonly Dictionary<string, float?> ModelHeightCache = new(StringComparer.OrdinalIgnoreCase);

	public static void ApplyGroundOffset(Node3D glbRoot)
	{
		if (!TryGetCombinedMeshBoundsInRootSpace(glbRoot, out var combined))
		{
			return;
		}

		var minY = combined.Position.Y;
		if (Mathf.Abs(minY) < 0.0001f)
		{
			return;
		}

		glbRoot.Position += new Vector3(0f, -minY, 0f);
	}

	public static float GetSpawnOriginYOffset(string modelName, string modelsDirectory = DefaultModelsDirectory)
	{
		return TryGetModelBoundsHeight(modelName, modelsDirectory, out var height) ? height * 0.5f : 0f;
	}

	public static float GetEditorVisualExtraYOffset(string modelName, string modelsDirectory = DefaultModelsDirectory)
	{
		return TryGetModelBoundsHeight(modelName, modelsDirectory, out var height) ? height * 0.5f : 0f;
	}

	public static bool TryGetModelBoundsHeight(
		string modelName,
		string modelsDirectory,
		out float height)
	{
		height = 0f;
		if (string.IsNullOrWhiteSpace(modelName))
		{
			return false;
		}

		if (ModelHeightCache.TryGetValue(modelName, out var cached))
		{
			if (!cached.HasValue)
			{
				return false;
			}

			height = cached.Value;
			return height > 0f;
		}

		var glbPath = $"{modelsDirectory.TrimEnd('/')}/{modelName}.glb";
		if (!ResourceLoader.Exists(glbPath))
		{
			ModelHeightCache[modelName] = null;
			return false;
		}

		var packed = ResourceLoader.Load<PackedScene>(glbPath);
		if (packed is null)
		{
			ModelHeightCache[modelName] = null;
			return false;
		}

		var glbRoot = packed.Instantiate<Node3D>();
		try
		{
			ApplyGroundOffset(glbRoot);
			if (!TryGetCombinedMeshBoundsInRootSpace(glbRoot, out var combined) || combined.Size.Y <= 0f)
			{
				ModelHeightCache[modelName] = null;
				return false;
			}

			height = combined.Size.Y;
			ModelHeightCache[modelName] = height;
			return true;
		}
		finally
		{
			glbRoot.QueueFree();
		}
	}
}
