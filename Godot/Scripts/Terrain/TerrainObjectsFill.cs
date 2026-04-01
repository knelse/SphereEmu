using System;
using System.Collections.Generic;
using Godot;
using Newtonsoft.Json;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
/// Editor tool: loads object placements from <c>Godot/Terrain/ObjectData/*.json</c>, instantiates GLB scenes from
/// <see cref="ModelsDirectory"/>, and clears/rebuilds three category nodes on each run.
/// </summary>
[Tool]
public partial class TerrainObjectsFill : Node3D
{
	public const string PlantsNodeName = "TerrainPlants";
	public const string RocksNodeName = "TerrainRocks";
	public const string OtherNodeName = "TerrainOther";

	[Export]
	public string ObjectDataDirectory { get; set; } = "res://Godot/Terrain/ObjectData/";

	[Export]
	public string ModelsDirectory { get; set; } = "res://Godot/Models/";

	[ExportToolButton ("Rebuild terrain objects")]
	public Callable RebuildTerrainObjectsButton => Callable.From (RebuildTerrainObjects);

	/// <summary>Clears category children and repopulates from all JSON files in <see cref="ObjectDataDirectory"/>.</summary>
	public void RebuildTerrainObjects ()
	{
		var dir = ObjectDataDirectory.TrimEnd ('/') + "/";
		if (!DirAccess.DirExistsAbsolute (ProjectSettings.GlobalizePath (dir)))
		{
			GD.PushError ($"TerrainObjectsFill: directory not found: {ObjectDataDirectory}");
			return;
		}

		var plants = GetOrCreateCategory (PlantsNodeName);
		var rocks = GetOrCreateCategory (RocksNodeName);
		var other = GetOrCreateCategory (OtherNodeName);

		ClearChildren (plants);
		ClearChildren (rocks);
		ClearChildren (other);

		var sceneCache = new Dictionary<string, PackedScene?> ();
		var plantIndex = 0;
		var rockIndex = 0;
		var otherIndex = 0;

		var da = DirAccess.Open (dir);
		if (da is null)
		{
			GD.PushError ($"TerrainObjectsFill: could not open: {ObjectDataDirectory}");
			return;
		}

		da.ListDirBegin ();
		while (true)
		{
			var name = da.GetNext ();
			if (name == string.Empty)
			{
				break;
			}

			if (da.CurrentIsDir () || !name.EndsWith (".json", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var path = dir + name;
			var jsonText = global::Godot.FileAccess.GetFileAsString (path);
			if (string.IsNullOrEmpty (jsonText))
			{
				GD.PushWarning ($"TerrainObjectsFill: empty or unreadable: {path}");
				continue;
			}

			List<TerrainObjectRecord>? records;
			try
			{
				records = JsonConvert.DeserializeObject<List<TerrainObjectRecord>> (jsonText);
			}
			catch (Exception ex)
			{
				GD.PushWarning ($"TerrainObjectsFill: JSON parse failed ({path}): {ex.Message}");
				continue;
			}

			if (records is null)
			{
				continue;
			}

			foreach (var rec in records)
			{
				if (rec is null || string.IsNullOrWhiteSpace (rec.ObjectName))
				{
					continue;
				}

				var scene = GetOrLoadScene (rec.ObjectName, sceneCache);
				if (scene is null)
				{
					GD.PushWarning ($"TerrainObjectsFill: no model for '{rec.ObjectName}' (tried .glb / .gltf under {ModelsDirectory})");
					continue;
				}

				var instance = scene.Instantiate<Node3D> ();
				var pos = rec.Coordinates?.ToVector3 () ?? Vector3.Zero;
				var rot = rec.RotationEuler?.ToEulerRadians () ?? Vector3.Zero;
				instance.Position = pos;
				instance.Rotation = rot;

				var lower = rec.ObjectName.ToLowerInvariant ();
				Node3D parent;
				string baseName;
				if (lower.Contains ("tree") || lower.Contains ("bush"))
				{
					parent = plants;
					baseName = $"{rec.ObjectName}_{plantIndex++}";
				}
				else if (lower.Contains ("rock") || lower.Contains ("stone"))
				{
					parent = rocks;
					baseName = $"{rec.ObjectName}_{rockIndex++}";
				}
				else
				{
					parent = other;
					baseName = $"{rec.ObjectName}_{otherIndex++}";
				}

				instance.Name = baseName;
				parent.AddChild (instance);
				SetOwnerIfEditor (instance);
			}
		}

		da.ListDirEnd ();
		da.Dispose ();
	}

	private Node3D GetOrCreateCategory (string nodeName)
	{
		if (GetNodeOrNull (nodeName) is Node3D existing)
		{
			return existing;
		}

		var n = new Node3D { Name = nodeName };
		AddChild (n);
		SetOwnerIfEditor (n);
		return n;
	}

	private static void ClearChildren (Node3D node)
	{
		foreach (var child in node.GetChildren ())
		{
			child.QueueFree ();
		}
	}

	private PackedScene? GetOrLoadScene (string objectName, Dictionary<string, PackedScene?> cache)
	{
		if (cache.TryGetValue (objectName, out var cached))
		{
			return cached;
		}

		var baseDir = ModelsDirectory.TrimEnd ('/') + "/";
		PackedScene? scene = null;
		foreach (var ext in new[] { "glb", "gltf" })
		{
			var path = $"{baseDir}{objectName}.{ext}";
			if (ResourceLoader.Exists (path))
			{
				scene = ResourceLoader.Load<PackedScene> (path);
				break;
			}
		}

		cache[objectName] = scene;
		return scene;
	}

	private void SetOwnerIfEditor (Node node)
	{
		if (!Engine.IsEditorHint ())
		{
			return;
		}

		var root = GetTree ()?.EditedSceneRoot;
		node.Owner = root ?? this;
	}

	private sealed class TerrainObjectRecord
	{
		[JsonProperty ("object_name")]
		public string ObjectName { get; set; } = string.Empty;

		[JsonProperty ("coordinates")]
		public TerrainCoordinates? Coordinates { get; set; }

		[JsonProperty ("rotation_euler")]
		public TerrainRotationEuler? RotationEuler { get; set; }
	}

	private sealed class TerrainCoordinates
	{
		[JsonProperty ("x")]
		public double X { get; set; }

		[JsonProperty ("y")]
		public double Y { get; set; }

		[JsonProperty ("z")]
		public double Z { get; set; }

		public Vector3 ToVector3 () => new ((float) X, (float) Y, (float) Z);
	}

	/// <summary>JSON uses yaw (Y), pitch (X), roll (Z) in radians — matches <see cref="Node3D.Rotation"/>.</summary>
	private sealed class TerrainRotationEuler
	{
		[JsonProperty ("yaw")]
		public double Yaw { get; set; }

		[JsonProperty ("pitch")]
		public double Pitch { get; set; }

		[JsonProperty ("roll")]
		public double Roll { get; set; }

		public Vector3 ToEulerRadians () =>
			new ((float) Pitch, (float) Yaw, (float) Roll);
	}
}
