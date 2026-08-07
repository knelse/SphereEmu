using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;
using SphServer.Godot.Scripts.Util;

namespace SphServer.Godot.Scripts.World;

public readonly record struct WorldContentEntry(
	WorldContentKind Kind,
	int TileX,
	int TileZ,
	Vector3 Position,
	string NodeName,
	Vector3[] Slots);

/// <summary>
///     Always-resident spatial index + baked spawn-slot sidecar for world placement chunks.
///     Binary format <c>WCIDX002</c> under <see cref="DefaultIndexPath"/>.
/// </summary>
public sealed class WorldContentIndex
{
	public const string DefaultIndexPath = "res://Godot/World/world_content_index.bin";
	private const string Magic = "WCIDX002";

	private static WorldContentIndex? Loaded;
	private static readonly object LoadLock = new();

	private readonly List<WorldContentEntry> entries = [];
	private readonly Dictionary<(WorldContentKind Kind, int TileX, int TileZ), List<int>> byTile = new();
	private readonly Dictionary<(WorldContentKind Kind, string NodeName), int> byName = new();
	private readonly HashSet<(WorldContentKind Kind, int TileX, int TileZ)> chunkTiles = [];

	public IReadOnlyList<WorldContentEntry> Entries => entries;

	public static WorldContentIndex GetOrLoad(string path = DefaultIndexPath)
	{
		lock (LoadLock)
		{
			if (Loaded is not null)
			{
				return Loaded;
			}

			Loaded = new WorldContentIndex();
			Loaded.TryLoadFrom(path);
			return Loaded;
		}
	}

	public static void ReplaceLoaded(WorldContentIndex index)
	{
		lock (LoadLock)
		{
			Loaded = index;
		}
	}

	public static void ClearLoaded()
	{
		lock (LoadLock)
		{
			Loaded = null;
		}
	}

	public bool HasChunk(WorldContentKind kind, int tileX, int tileZ) =>
		chunkTiles.Contains((kind, tileX, tileZ));

	public IEnumerable<(WorldContentKind Kind, int TileX, int TileZ)> EnumerateChunkTiles() => chunkTiles;

	public bool TryGetSlots(WorldContentKind kind, string nodeName, out Vector3[] slots)
	{
		slots = [];
		if (!byName.TryGetValue((kind, nodeName), out var index))
		{
			return false;
		}

		slots = entries[index].Slots;
		return slots.Length > 0;
	}

	public void Clear()
	{
		entries.Clear();
		byTile.Clear();
		byName.Clear();
		chunkTiles.Clear();
	}

	public void AddOrReplace(WorldContentEntry entry)
	{
		var key = (entry.Kind, entry.NodeName);
		if (byName.TryGetValue(key, out var existing))
		{
			var old = entries[existing];
			RemoveFromTileIndex(existing, old);
			entries[existing] = entry;
			AddToTileIndex(existing, entry);
		}
		else
		{
			var index = entries.Count;
			entries.Add(entry);
			byName[key] = index;
			AddToTileIndex(index, entry);
		}

		chunkTiles.Add((entry.Kind, entry.TileX, entry.TileZ));
	}

	public void MarkChunkTile(WorldContentKind kind, int tileX, int tileZ) =>
		chunkTiles.Add((kind, tileX, tileZ));

	public IEnumerable<(int TileX, int TileZ)> TilesNear(Vector3 worldCenter, float radiusMeters)
	{
		var (centerX, centerZ) = WorldTileKeys.FromWorld(worldCenter);
		var tileRadius = (int)Math.Ceiling(radiusMeters / WorldTileKeys.TileSizeWorld) + 1;
		for (var tz = centerZ - tileRadius; tz <= centerZ + tileRadius; tz++)
		{
			for (var tx = centerX - tileRadius; tx <= centerX + tileRadius; tx++)
			{
				yield return (tx, tz);
			}
		}
	}

	public bool TryLoadFrom(string resPath)
	{
		Clear();
		if (!ResPathIO.TryReadAllBytes(resPath, out var bytes))
		{
			if (ResPathIO.IsVirtualPath(resPath))
			{
				GD.PushWarning($"WorldContentIndex: not found: {resPath}");
			}

			return false;
		}

		using var stream = new MemoryStream(bytes, writable: false);
		using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
		var magicBytes = reader.ReadBytes(8);
		var magic = Encoding.ASCII.GetString(magicBytes);
		if (magic != Magic)
		{
			GD.PushError($"WorldContentIndex: unexpected magic '{magic}' in {resPath}");
			return false;
		}

		var entryCount = reader.ReadInt32();
		var chunkCount = reader.ReadInt32();
		for (var i = 0; i < chunkCount; i++)
		{
			var kind = (WorldContentKind)reader.ReadByte();
			var tileX = reader.ReadInt16();
			var tileZ = reader.ReadInt16();
			chunkTiles.Add((kind, tileX, tileZ));
		}

		for (var i = 0; i < entryCount; i++)
		{
			var kind = (WorldContentKind)reader.ReadByte();
			var tileX = reader.ReadInt16();
			var tileZ = reader.ReadInt16();
			var px = reader.ReadSingle();
			var py = reader.ReadSingle();
			var pz = reader.ReadSingle();
			var nameLen = reader.ReadUInt16();
			var name = Encoding.UTF8.GetString(reader.ReadBytes(nameLen));
			var slotCount = reader.ReadUInt16();
			var slots = new Vector3[slotCount];
			for (var s = 0; s < slotCount; s++)
			{
				slots[s] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			}

			AddOrReplace(new WorldContentEntry(kind, tileX, tileZ, new Vector3(px, py, pz), name, slots));
		}

		return true;
	}

	public void SaveTo(string resPath)
	{
		var absolute = ProjectSettings.GlobalizePath(resPath);
		var directory = Path.GetDirectoryName(absolute);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		using var stream = File.Create(absolute);
		using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
		writer.Write(Encoding.ASCII.GetBytes(Magic));
		writer.Write(entries.Count);
		writer.Write(chunkTiles.Count);
		foreach (var (kind, tileX, tileZ) in chunkTiles)
		{
			writer.Write((byte)kind);
			writer.Write((short)tileX);
			writer.Write((short)tileZ);
		}

		foreach (var entry in entries)
		{
			writer.Write((byte)entry.Kind);
			writer.Write((short)entry.TileX);
			writer.Write((short)entry.TileZ);
			writer.Write(entry.Position.X);
			writer.Write(entry.Position.Y);
			writer.Write(entry.Position.Z);
			var nameBytes = Encoding.UTF8.GetBytes(entry.NodeName);
			if (nameBytes.Length > ushort.MaxValue)
			{
				throw new InvalidOperationException($"Node name too long: {entry.NodeName}");
			}

			writer.Write((ushort)nameBytes.Length);
			writer.Write(nameBytes);
			if (entry.Slots.Length > ushort.MaxValue)
			{
				throw new InvalidOperationException($"Too many slots on {entry.NodeName}");
			}

			writer.Write((ushort)entry.Slots.Length);
			foreach (var slot in entry.Slots)
			{
				writer.Write(slot.X);
				writer.Write(slot.Y);
				writer.Write(slot.Z);
			}
		}
	}

	private void AddToTileIndex(int index, WorldContentEntry entry)
	{
		var tileKey = (entry.Kind, entry.TileX, entry.TileZ);
		if (!byTile.TryGetValue(tileKey, out var list))
		{
			list = [];
			byTile[tileKey] = list;
		}

		list.Add(index);
	}

	private void RemoveFromTileIndex(int index, WorldContentEntry old)
	{
		var tileKey = (old.Kind, old.TileX, old.TileZ);
		if (byTile.TryGetValue(tileKey, out var list))
		{
			list.Remove(index);
		}
	}
}
