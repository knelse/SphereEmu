using System.Collections.Generic;
using Godot;
using SphServer.Godot.Scripts.Objects.HelperGizmos;
using SphServer.Helpers;

namespace SphServer.Godot.Scripts.Objects;

/// <summary>
/// Editor tool: rebuilds teleport point helper gizmos from <see cref="SavedCoords.TeleportPoints"/>.
/// </summary>
[Tool]
public partial class TeleportPointFill : Node3D
{
	[Export]
	public string TeleportPointScenePath { get; set; } = "res://Godot/Scenes/teleport_point_helper.tscn";

	[ExportToolButton("Rebuild teleport points")]
	public Callable RebuildTeleportPointsButton => Callable.From(RebuildTeleportPoints);

	public void RebuildTeleportPoints()
	{
		foreach (var child in GetChildren())
		{
			child.Free();
		}

		if (!WorldObjectDumpFillCommon.TryLoadPackedScene(TeleportPointScenePath, "TeleportPointFill", out var scene))
		{
			return;
		}

		var seenSourcePositions = new HashSet<(long Qx, long Qy, long Qz)>();
		var continentNodes = new Dictionary<Continents, Node3D>();
		var poiTypeNodes = new Dictionary<(Continents Continent, PoiType PoiType), Node3D>();
		var entriesConsidered = 0;
		var spawned = 0;
		var duplicateSkipped = 0;

		foreach (var (continent, poiTypes) in SavedCoords.TeleportPoints)
		{
			foreach (var (poiType, points) in poiTypes)
			{
				var poiTypeParent = GetOrCreatePoiTypeParent(continent, poiType, continentNodes, poiTypeNodes);

				foreach (var (pointName, coords) in points)
				{
					entriesConsidered++;

					var posKey = WorldObjectDumpFillCommon.QuantizeSourcePosition(coords.x, coords.y, coords.z);
					if (!seenSourcePositions.Add(posKey))
					{
						duplicateSkipped++;
						continue;
					}

					var instance = scene!.Instantiate<TeleportPointHelper>();
					instance.Name = $"TP_{pointName}";
					instance.Position = new Vector3((float)coords.x, -(float)coords.y, -(float)coords.z);
					instance.Continent = continent;
					instance.PoiType = poiType;
					instance.PointName = pointName;
					instance.SetCoords(coords);

					poiTypeParent.AddChild(instance);
					WorldObjectDumpFillCommon.SetOwnerIfEditor(this, instance);
					spawned++;
				}
			}
		}

		GD.Print(
			$"TeleportPointFill: considered={entriesConsidered}, spawned={spawned}, dupSkipped={duplicateSkipped}");
	}

	private Node3D GetOrCreatePoiTypeParent(
		Continents continent,
		PoiType poiType,
		Dictionary<Continents, Node3D> continentNodes,
		Dictionary<(Continents Continent, PoiType PoiType), Node3D> poiTypeNodes)
	{
		var key = (continent, poiType);
		if (poiTypeNodes.TryGetValue(key, out var existing))
		{
			return existing;
		}

		if (!continentNodes.TryGetValue(continent, out var continentNode))
		{
			continentNode = new Node3D { Name = GetContinentNodeName(continent) };
			AddChild(continentNode);
			WorldObjectDumpFillCommon.SetOwnerIfEditor(this, continentNode);
			continentNodes[continent] = continentNode;
		}

		var poiTypeNode = new Node3D { Name = poiType.ToString() };
		continentNode.AddChild(poiTypeNode);
		WorldObjectDumpFillCommon.SetOwnerIfEditor(this, poiTypeNode);
		poiTypeNodes[key] = poiTypeNode;
		return poiTypeNode;
	}

	private static string GetContinentNodeName(Continents continent) =>
		continent switch
		{
			Continents.Гиперион => "Hyperion",
			Continents.Харон => "Haron",
			Continents.Феб => "Feb",
			Continents.Родос => "Rodos",
			_ => continent.ToString()
		};
}
