using System.IO;
using Godot;
using SphServer.Godot.Scripts.Util;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
///     Bake outputs live under <c>GodotAssetSource/TerrainBake/</c> (git-tracked) with a
///     parent <c>.gdignore</c> so Godot does not import/scan them.
///     Editor/dev: absolute disk paths. Slim exports: <c>res://</c> after the terrainbake zip pack is mounted.
/// </summary>
public static class TerrainBakePaths
{
	public const string FolderName = "TerrainBake";
	public const string AssetSourceFolderName = "GodotAssetSource";
	public const string ResRoot = "res://GodotAssetSource/TerrainBake";

	public static string RootDir
	{
		get
		{
			// Prefer on-disk bake tree (editor / checkout with GodotAssetSource).
			foreach (var candidate in CandidateRoots())
			{
				if (Directory.Exists(candidate))
				{
					return candidate;
				}
			}

			// Slim exports: terrainbake zip mounted as res:// (no disk copy).
			using (var da = DirAccess.Open(ResRoot))
			{
				if (da is not null)
				{
					return ResRoot;
				}
			}

			// Default write location for bakers (editor / first bake).
			return Path.GetFullPath(Path.Combine(ProjectDir, AssetSourceFolderName, FolderName));
		}
	}

	public static bool UsesVirtualResPaths => ResPathIO.IsVirtualPath(RootDir);

	public static string ProjectDir =>
		ProjectSettings.GlobalizePath("res://").TrimEnd('/', '\\');

	public static string GroundChunksDir => Combine("GroundChunks");
	public static string GroundMeshesDir => Combine("GroundMeshes");
	public static string GroundShapesDir => Combine("GroundShapes");
	public static string GeneratedNavMeshesDir => Combine("GeneratedNavMeshes");
	public static string GeneratedMultiMeshesDir => Combine("GeneratedMultiMeshes");
	public static string GeneratedIndoorNavMeshesDir => Combine("GeneratedIndoorNavMeshes");

	/// <summary>
	///     Full MeshLibrary used only by <c>TerrainGridFill</c> rebuilds. Kept under
	///     <c>GodotAssetSource/Terrain/</c> (parent <c>.gdignore</c>) so opening the project does not
	///     import/parse this ~300MB LFS file.
	/// </summary>
	public static string MeshLibraryTres =>
		Path.GetFullPath(Path.Combine(ProjectDir, AssetSourceFolderName, "Terrain", "TerrainMeshLibrary.tres"))
			.Replace('\\', '/');

	/// <summary>
	///     Absolute filesystem path, or <c>res://…</c> when the bake tree is pack-mounted.
	/// </summary>
	public static string Combine(params string[] relativeParts)
	{
		var root = RootDir;
		if (ResPathIO.IsVirtualPath(root))
		{
			return ResPathIO.JoinVirtual(root, relativeParts);
		}

		var parts = new string[relativeParts.Length + 1];
		parts[0] = root;
		Array.Copy(relativeParts, 0, parts, 1, relativeParts.Length);
		return Path.GetFullPath(Path.Combine(parts)).Replace('\\', '/');
	}

	public static string NavMeshRes(string tileKey) =>
		Combine("GeneratedNavMeshes", $"{tileKey}.res");

	public static string IndoorClusterRes(int id) =>
		Combine("GeneratedIndoorNavMeshes", $"cluster_{id}.res");

	public static string IndoorIndexJson =>
		Combine("GeneratedIndoorNavMeshes", "index.json");

	public static string GroundChunkScene(int gx, int gz) =>
		Combine("GroundChunks", $"{gx}_{gz}.tscn");

	public static string GroundMeshRes(string masterName) =>
		Combine("GroundMeshes", $"{masterName}.res");

	public static string GroundShapeRes(string masterName) =>
		Combine("GroundShapes", $"{masterName}.res");

	public static string MultiMeshRes(string fileName) =>
		Combine("GeneratedMultiMeshes", fileName);

	public static void EnsureDirectory(string absDir)
	{
		if (ResPathIO.IsVirtualPath(absDir))
		{
			throw new InvalidOperationException($"Cannot create virtual path directory: {absDir}");
		}

		Directory.CreateDirectory(absDir.Replace('/', Path.DirectorySeparatorChar));
	}

	/// <summary>
	///     Like <see cref="EnsureDirectory"/>, but on Windows renames an existing directory when only
	///     casing differs. <see cref="Directory.CreateDirectory"/> is a no-op for case-only mismatches,
	///     which leaves Godot complaining that <c>res://…/terrainother/…</c> is stored as
	///     <c>TerrainOther</c> on disk.
	/// </summary>
	public static void EnsureDirectoryExactCase(string absDir)
	{
		if (ResPathIO.IsVirtualPath(absDir))
		{
			throw new InvalidOperationException($"Cannot create virtual path directory: {absDir}");
		}

		var normalized = Path.GetFullPath(absDir.Replace('/', Path.DirectorySeparatorChar));
		var parent = Path.GetDirectoryName(normalized);
		var desiredName = Path.GetFileName(normalized);
		if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(desiredName))
		{
			EnsureDirectory(normalized);
			return;
		}

		EnsureDirectory(parent);

		string? existing = null;
		foreach (var dir in Directory.GetDirectories(parent))
		{
			if (string.Equals(Path.GetFileName(dir), desiredName, StringComparison.OrdinalIgnoreCase))
			{
				existing = dir;
				break;
			}
		}

		if (existing is null)
		{
			Directory.CreateDirectory(normalized);
			return;
		}

		if (string.Equals(Path.GetFileName(existing), desiredName, StringComparison.Ordinal))
		{
			return;
		}

		// Windows requires a two-step rename for case-only changes.
		var temp = Path.Combine(parent, "__casefix_" + Guid.NewGuid().ToString("N"));
		Directory.Move(existing, temp);
		Directory.Move(temp, Path.Combine(parent, desiredName));
	}

	public static void EnsureBakeRoot()
	{
		EnsureDirectory(RootDir);
		var assetSourceRoot = Path.Combine(ProjectDir, AssetSourceFolderName);
		EnsureDirectory(assetSourceRoot);
		var assetIgnore = Path.Combine(assetSourceRoot, ".gdignore");
		if (!File.Exists(assetIgnore))
		{
			File.WriteAllText(assetIgnore, "");
		}
	}

	private static IEnumerable<string> CandidateRoots()
	{
		var env = global::System.Environment.GetEnvironmentVariable("TERRAIN_BAKE_PATH");
		if (!string.IsNullOrWhiteSpace(env))
		{
			yield return Path.GetFullPath(env);
		}

		yield return Path.GetFullPath(Path.Combine(ProjectDir, AssetSourceFolderName, FolderName));
		// Legacy location (pre-GodotAssetSource move).
		yield return Path.GetFullPath(Path.Combine(ProjectDir, FolderName));

		var exeDir = Path.GetDirectoryName(OS.GetExecutablePath());
		if (!string.IsNullOrEmpty(exeDir))
		{
			yield return Path.GetFullPath(Path.Combine(exeDir, AssetSourceFolderName, FolderName));
			yield return Path.GetFullPath(Path.Combine(exeDir, FolderName));
		}
	}
}
