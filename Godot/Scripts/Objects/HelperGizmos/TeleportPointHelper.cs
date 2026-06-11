using Godot;
using SphServer.Helpers;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

[Tool]
public partial class TeleportPointHelper : MeshInstance3D
{
	[Export]
	public Continents Continent { get; set; }

	[Export]
	public PoiType PoiType { get; set; }

	[Export(PropertyHint.None, "Name")]
	public string PointName { get; set; } = "";

	public WorldCoords? Coords { get; private set; }

	[Export]
	public string CoordsPreview { get; private set; } = "";

	public void SetCoords(WorldCoords coords)
	{
		Coords = coords;
		CoordsPreview = coords.ToDebugString();
	}
}
