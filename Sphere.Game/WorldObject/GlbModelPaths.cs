using Godot;

namespace SphServer.Sphere.Game.WorldObject;

/// <summary>
///     Resolves <c>res://Godot/Models/…</c> GLB/GLTF paths using the real on-disk filename casing.
///     Loading with a differently-cased path works on Windows but poisons NTFS casing and triggers
///     Godot "Case mismatch" warnings for sibling textures on case-sensitive exports.
/// </summary>
internal static class GlbModelPaths
{
	private static readonly object Gate = new();
	private static readonly Dictionary<string, string> FileNameByLower = new(StringComparer.Ordinal);
	private static string IndexedDirectory = string.Empty;

	/// <summary>
	///     Returns <c>res://…/ActualCase.glb</c> for <paramref name="modelName" /> (any case), or null if missing.
	/// </summary>
	public static string? Resolve(
		string modelName,
		string modelsDirectory = GlbVisualGrounding.DefaultModelsDirectory)
	{
		if (string.IsNullOrWhiteSpace(modelName))
		{
			return null;
		}

		var dir = modelsDirectory.TrimEnd('/') + "/";
		EnsureIndex(dir);

		foreach (var ext in new[] { ".glb", ".gltf" })
		{
			var key = (modelName + ext).ToLowerInvariant();
			if (FileNameByLower.TryGetValue(key, out var actualName))
			{
				return dir + actualName;
			}
		}

		return null;
	}

	/// <summary>Like <see cref="Resolve" />, but falls back to a naive <c>{name}.glb</c> path when missing.</summary>
	public static string ResolveOrFallback(
		string modelName,
		string modelsDirectory = GlbVisualGrounding.DefaultModelsDirectory)
	{
		return Resolve(modelName, modelsDirectory)
			   ?? $"{modelsDirectory.TrimEnd('/')}/{modelName}.glb";
	}

	private static void EnsureIndex(string dirWithSlash)
	{
		lock (Gate)
		{
			if (IndexedDirectory == dirWithSlash && FileNameByLower.Count > 0)
			{
				return;
			}

			FileNameByLower.Clear();
			IndexedDirectory = dirWithSlash;

			using var da = DirAccess.Open(dirWithSlash);
			if (da is null)
			{
				return;
			}

			da.ListDirBegin();
			while (true)
			{
				var name = da.GetNext();
				if (string.IsNullOrEmpty(name))
				{
					break;
				}

				if (da.CurrentIsDir() || name.StartsWith('.'))
				{
					continue;
				}

				if (!name.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)
					&& !name.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				FileNameByLower[name.ToLowerInvariant()] = name;
			}
		}
	}
}
