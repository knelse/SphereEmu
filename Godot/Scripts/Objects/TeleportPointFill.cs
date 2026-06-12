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
		WorldObjectDumpFillCommon.ClearRebuildableChildren(this);

		if (!WorldObjectDumpFillCommon.TryLoadPackedScene(TeleportPointScenePath, "TeleportPointFill", out var scene))
		{
			return;
		}

		var seenSourcePositions = new HashSet<(long Qx, long Qy, long Qz)>();
		WorldObjectDumpFillCommon.SeedSeenSourcePositions(this, seenSourcePositions);
		var continentNodes = new Dictionary<Continents, Node3D>();
		var poiTypeNodes = new Dictionary<(Continents Continent, PoiType PoiType), Node3D>();
		SeedExistingHierarchy(continentNodes, poiTypeNodes);
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

	private void SeedExistingHierarchy(
		Dictionary<Continents, Node3D> continentNodes,
		Dictionary<(Continents Continent, PoiType PoiType), Node3D> poiTypeNodes)
	{
		foreach (var child in GetChildren())
		{
			if (child is not Node3D continentNode)
			{
				continue;
			}

			if (!TryParseContinentNodeName(continentNode.Name, out var continent))
			{
				continue;
			}

			continentNodes[continent] = continentNode;

			foreach (var poiChild in continentNode.GetChildren())
			{
				if (poiChild is not Node3D poiNode)
				{
					continue;
				}

				if (!Enum.TryParse(poiNode.Name.ToString(), out PoiType poiType))
				{
					continue;
				}

				poiTypeNodes[(continent, poiType)] = poiNode;
			}
		}
	}

	private static bool TryParseContinentNodeName(StringName name, out Continents continent)
	{
		switch (name.ToString())
		{
			case "Hyperion":
				continent = Continents.Гиперион;
				return true;
			case "Haron":
				continent = Continents.Харон;
				return true;
			case "Feb":
				continent = Continents.Феб;
				return true;
			case "Rodos":
				continent = Continents.Родос;
				return true;
			default:
				return Enum.TryParse(name.ToString(), out continent);
		}
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
