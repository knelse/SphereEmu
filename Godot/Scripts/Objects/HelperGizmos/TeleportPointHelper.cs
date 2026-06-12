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

	/// <summary>
	///     When set in the editor, <see cref="TeleportPointFill"/> keeps this gizmo and treats its coordinates as occupied.
	/// </summary>
	[Export(PropertyHint.None, "Do Not Rebuild")]
	public bool DoNotRebuild { get; set; }

	public WorldCoords? Coords { get; private set; }

	[Export]
	public double X { get; private set; }

	[Export]
	public double Y { get; private set; }

	[Export]
	public double Z { get; private set; }

	[Export]
	public double Angle { get; private set; }

	public void SetCoords(WorldCoords coords)
	{
		Coords = coords;
		X = coords.x;
		Y = coords.y;
		Z = coords.z;
		Angle = coords.turn;
	}
}
