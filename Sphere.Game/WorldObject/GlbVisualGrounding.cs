using Godot;
using SphServer.Shared.GameData.Enums;

namespace SphServer.Sphere.Game.WorldObject;

/// <summary>
///     Shared GLB mesh bounds and feet-on-ground offset used by <see cref="WorldObject" /> and
///     <see cref="MonsterMultiMeshVisuals" />.
/// </summary>
internal static partial class GlbVisualGrounding
{
	public const string DefaultModelsDirectory = "res://Godot/Models/";

	/// <summary>Fixed Godot-Y spawn lift magnitude (replaces half-AABB for network placement).</summary>
	public const float SpawnOriginYOffsetMeters = 1f;

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

	/// <summary>
	///     Packet space flips Godot Y (<c>gameY = -godotY</c>). Negative Godot Y → add offset;
	///     non-negative → subtract offset.
	/// </summary>
	public static float SignSpawnOriginYOffset(float godotY, float offsetAbs = SpawnOriginYOffsetMeters)
	{
		if (offsetAbs <= 0f)
		{
			return 0f;
		}

		return godotY < 0f ? offsetAbs : -offsetAbs;
	}

	/// <summary>Signed spawn-slot Y delta for monsters (fixed <see cref="SpawnOriginYOffsetMeters" />).</summary>
	public static float GetSpawnOriginYOffsetForMonsterType(MonsterType monsterType, float godotY)
		=> SignSpawnOriginYOffset(godotY);

	/// <summary>Signed spawn-slot Y delta for alchemy GameObject IDs (fixed <see cref="SpawnOriginYOffsetMeters" />).</summary>
	public static float GetSpawnOriginYOffsetForGameObjectId(int gameObjectId, float godotY)
		=> SignSpawnOriginYOffset(godotY);

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
