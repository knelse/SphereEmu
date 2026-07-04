using System.Globalization;
using System.Collections.Generic;
using Godot;
using SphServer.Godot.Scripts.Objects.HelperGizmos;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects.Fill;

/// <summary>
/// Shared helpers for editor Fill tools that read tab/space-separated dumps:
/// ID, (skip), (skip), X, Y, Z, Angle, (optional extra ints...).
/// Source space matches terrain: <c>(x,y,z)_src ↦ (x,-y,-z)</c>.
/// Rows with duplicate source coordinates (same X, Y, Z columns) are skipped after the first occurrence.
/// </summary>
public static class WorldObjectDumpFillCommon
{
	public static int FloorCoordToInt(double v) =>
		(int)Math.Floor(v);

	public static string BuildPlacementName(string objectTypeName, int id, double x, double y, double z)
	{
		var ix = FloorCoordToInt(x);
		var iy = FloorCoordToInt(y);
		var iz = FloorCoordToInt(z);
		return $"{objectTypeName}_{id:X4}_[{ix}]_[{iy}]_[{iz}]";
	}

	/// <summary>
	/// DoorsFill-style iteration: parse from the bottom so later file rows "win" when deduping by position.
	/// Skips blank and comment lines (starting with <c>#</c> after trimming leading whitespace).
	/// </summary>
	public static IEnumerable<(int LineNumber, string[] Parts)> EnumerateDataLinesBottomUp(string text)
	{
		var lines = text.Split('\n');
		for (var i = lines.Length - 1; i >= 0; i--)
		{
			var lineNumber = i + 1;
			var line = lines[i].TrimEnd('\r');
			if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
			{
				continue;
			}

			var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
			yield return (lineNumber, parts);
		}
	}

	/// <summary>
	/// Heuristic: returns true if <paramref name="token"/> looks like a type string (not numeric).
	/// </summary>
	public static bool LooksLikeTypeToken(string token)
	{
		var t = token.Trim();
		if (t.Length == 0)
		{
			return false;
		}

		// If it has any letter, treat as a type label.
		for (var i = 0; i < t.Length; i++)
		{
			if (char.IsLetter(t[i]))
			{
				return true;
			}
		}

		return false;
	}

	public static bool MatchesTypeTokenIfPresent(string[] parts, int typeIndex, string expectedTypeValue)
	{
		if (parts.Length <= typeIndex)
		{
			return true;
		}

		var token = parts[typeIndex].Trim();
		if (!LooksLikeTypeToken(token))
		{
			return true;
		}

		return token.Equals(expectedTypeValue, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Mirrors DoorsFill "weird target" checks for placement coordinates.
	/// </summary>
	public static bool LooksLikeWeirdCoordToken(string s)
	{
		var t = s.Trim();
		return t.Contains("e-", StringComparison.OrdinalIgnoreCase);
	}

	public static bool IsWeirdCoordValue(double v)
	{
		if (double.IsNaN(v) || double.IsInfinity(v))
		{
			return true;
		}

		return Math.Abs(v) > 10000.0;
	}

	public static bool IsAllIntZero(double x, double y, double z) =>
		((int)x == 0 && (int)y == 0 && (int)z == 0);

	public static bool ShouldSkipWeirdCoords(string xToken, string yToken, string zToken, double x, double y, double z)
	{
		if (LooksLikeWeirdCoordToken(xToken) || LooksLikeWeirdCoordToken(yToken) || LooksLikeWeirdCoordToken(zToken))
		{
			return true;
		}

		if (Math.Abs(y) > 3000.0)
		{
			return true;
		}

		if (IsWeirdCoordValue(x) || IsWeirdCoordValue(y) || IsWeirdCoordValue(z) || IsAllIntZero(x, y, z))
		{
			return true;
		}

		return false;
	}

	public static bool TryLoadPackedScene(string scenePath, string logPrefix, out PackedScene? scene)
	{
		scene = null;
		if (!ResourceLoader.Exists(scenePath))
		{
			GD.PushError($"{logPrefix}: scene not found: {scenePath}");
			return false;
		}

		scene = ResourceLoader.Load<PackedScene>(scenePath);
		if (scene is null)
		{
			GD.PushError($"{logPrefix}: could not load: {scenePath}");
			return false;
		}

		return true;
	}

	public static bool TryReadTextFile(string path, string logPrefix, out string text)
	{
		text = string.Empty;

		if (!global::Godot.FileAccess.FileExists(path))
		{
			GD.PushError($"{logPrefix}: file not found: {path}");
			return false;
		}

		try
		{
			text = File.ReadAllText(path);
		}
		catch (Exception ex)
		{
			GD.PushWarning($"{logPrefix}: File.ReadAllText failed ({ex.Message}), falling back to Godot FileAccess");
			text = global::Godot.FileAccess.GetFileAsString(path);
		}

		if (string.IsNullOrWhiteSpace(text))
		{
			GD.PushWarning($"{logPrefix}: empty file: {path}");
			return false;
		}

		return true;
	}

	public static IEnumerable<(int LineNumber, string[] Parts)> EnumerateDataLines(string text)
	{
		var lineNumber = 0;
		using var sr = new StringReader(text);
		while (true)
		{
			var rawLine = sr.ReadLine();
			if (rawLine is null)
			{
				yield break;
			}

			lineNumber++;
			var line = rawLine.TrimEnd('\r');
			if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
			{
				continue;
			}

			var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
			yield return (lineNumber, parts);
		}
	}

	public static (long Qx, long Qy, long Qz) QuantizeSourcePosition(double x, double y, double z)
	{
		const double scale = 10000.0;
		return (
			(long)Math.Round(x * scale),
			(long)Math.Round(y * scale),
			(long)Math.Round(z * scale));
	}

	public static bool IsPreservedPlacement(Node node) =>
		node is WorldObject { DoNotRebuild: true } or TeleportPointHelper { DoNotRebuild: true };

	/// <summary>
	///     Removes generated placements under <paramref name="root" />, keeping nodes marked
	///     <see cref="WorldObject.DoNotRebuild" /> / <see cref="TeleportPointHelper.DoNotRebuild" />.
	///     Empty grouping nodes left behind after recursive cleanup are removed as well.
	/// </summary>
	public static int ClearRebuildableChildren(Node root)
	{
		var removed = 0;
		for (var i = root.GetChildCount() - 1; i >= 0; i--)
		{
			var child = root.GetChild(i);
			if (!GodotObject.IsInstanceValid(child))
			{
				continue;
			}

			if (IsPreservedPlacement(child))
			{
				continue;
			}

			if (child is WorldObject or TeleportPointHelper)
			{
				RemovePlacementNode(child);
				removed++;
				continue;
			}

			if (HasPreservedDescendant(child))
			{
				removed += ClearRebuildableChildren(child);
				if (GodotObject.IsInstanceValid(child) && child.GetChildCount() == 0)
				{
					RemovePlacementNode(child);
					removed++;
				}

				continue;
			}

			RemovePlacementNode(child);
			removed++;
		}

		return removed;
	}

	private static bool HasPreservedDescendant(Node node)
	{
		if (IsPreservedPlacement(node))
		{
			return true;
		}

		foreach (var child in node.GetChildren())
		{
			if (HasPreservedDescendant(child))
			{
				return true;
			}
		}

		return false;
	}

	private static void RemovePlacementNode(Node node)
	{
		if (!GodotObject.IsInstanceValid(node))
		{
			return;
		}

		node.Free();
	}

	/// <summary>
	///     Source-space dedup key from a placement's Godot position relative to the Fill root.
	/// </summary>
	public static (long Qx, long Qy, long Qz) SourcePositionKeyFromPlacementNode(Node3D placement, Node3D fillRoot)
	{
		var local = fillRoot.ToLocal(placement.GlobalTransform.Origin);
		return QuantizeSourcePosition(local.X, -local.Y, -local.Z);
	}

	/// <summary>
	///     Registers source coordinates of preserved placements so rebuild does not spawn duplicates at the same spot.
	/// </summary>
	public static int SeedSeenSourcePositions(Node3D fillRoot, HashSet<(long Qx, long Qy, long Qz)> seenSourcePositions)
	{
		var seeded = 0;
		SeedSeenSourcePositionsRecursive(fillRoot, fillRoot, seenSourcePositions, ref seeded);
		return seeded;
	}

	private static void SeedSeenSourcePositionsRecursive(
		Node node,
		Node3D fillRoot,
		HashSet<(long Qx, long Qy, long Qz)> seenSourcePositions,
		ref int seeded)
	{
		if (IsPreservedPlacement(node) && node is Node3D placement)
		{
			if (seenSourcePositions.Add(SourcePositionKeyFromPlacementNode(placement, fillRoot)))
			{
				seeded++;
			}

			return;
		}

		foreach (var child in node.GetChildren())
		{
			SeedSeenSourcePositionsRecursive(child, fillRoot, seenSourcePositions, ref seeded);
		}
	}

	/// <summary>
	///     Registers node names of preserved <see cref="WorldObject" /> placements (e.g. NPC rebuild name dedup).
	/// </summary>
	public static int SeedUsedNodeNamesFromPreservedPlacements(Node fillRoot, HashSet<string> usedNames)
	{
		var seeded = 0;
		foreach (var node in fillRoot.FindChildren("*", recursive: true))
		{
			if (node is not WorldObject { DoNotRebuild: true } wo)
			{
				continue;
			}

			if (usedNames.Add(wo.Name.ToString()))
			{
				seeded++;
			}
		}

		return seeded;
	}

	/// <summary>ID column is hex (optional <c>0x</c>).</summary>
	public static bool TryParseId(string s, out int id)
	{
		s = s.Trim();
		if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			s = s[2..];
		}

		return int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id);
	}

	public static bool TryParseDouble(string s, out double v) =>
		double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

	public static bool TryParseInt(string s, out int v) =>
		int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

	public static bool TryParseAngle(string s, out int angle)
	{
		if (int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out angle))
		{
			return true;
		}

		if (double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
		{
			angle = (int)Math.Round(d);
			return true;
		}

		angle = 0;
		return false;
	}

	public static void SetOwnerIfEditor(Node ownerFallback, Node node)
	{
		if (!Engine.IsEditorHint())
		{
			return;
		}

		var tree = ownerFallback.GetTree();
		var root = tree?.EditedSceneRoot;
		node.Owner = root ?? ownerFallback;
	}

	public static bool TryParseCommonPlacementColumns(
		string[] parts,
		out int id,
		out double x,
		out double y,
		out double z,
		out int angleEncoded)
	{
		id = 0;
		x = y = z = 0;
		angleEncoded = 0;

		if (parts.Length < 7)
		{
			return false;
		}

		if (!TryParseId(parts[0].Trim(), out id))
		{
			return false;
		}

		if (!TryParseDouble(parts[3], out x)
			|| !TryParseDouble(parts[4], out y)
			|| !TryParseDouble(parts[5], out z))
		{
			return false;
		}

		if (!TryParseAngle(parts[6], out angleEncoded))
		{
			return false;
		}

		return true;
	}
}

