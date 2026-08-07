using System.IO;
using Godot;

namespace SphServer.Godot.Scripts.World;

/// <summary>
///     Chunk path conventions and LoadAll / pack helpers for world placement scenes.
/// </summary>
public static class WorldChunkCatalog
{
	public const string ChunksRoot = "res://Godot/World/Chunks";
	public const string IndexPath = WorldContentIndex.DefaultIndexPath;

	public static string ChunkDirectory(WorldContentKind kind) =>
		$"{ChunksRoot}/{WorldContentKindPaths.FolderName(kind)}";

	public static string ChunkScenePath(WorldContentKind kind, int tileX, int tileZ) =>
		$"{ChunkDirectory(kind)}/{WorldTileKeys.FormatKey(tileX, tileZ)}.tscn";

	public static string ChunkScenePathAbsolute(WorldContentKind kind, int tileX, int tileZ) =>
		ProjectSettings.GlobalizePath(ChunkScenePath(kind, tileX, tileZ));

	public static void EnsureChunkDirectory(WorldContentKind kind)
	{
		var absolute = ProjectSettings.GlobalizePath(ChunkDirectory(kind));
		Directory.CreateDirectory(absolute);
	}

	public static bool ChunkExists(WorldContentKind kind, int tileX, int tileZ)
	{
		var path = ChunkScenePath(kind, tileX, tileZ);
		return ResourceLoader.Exists(path);
	}
}
