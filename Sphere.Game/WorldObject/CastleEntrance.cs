using Godot;
using SphServer.Godot.Scripts.Objects;
using SphServer.Helpers;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class CastleEntrance : WorldObject
{
	public CastleEntrance()
	{
		ObjectType = ObjectType.CastleEntrance;
		ModelName = "edoor";
	}

	[Export] public Castles Castle { get; set; }

	[ExportToolButton("Jump to tablet")]
	public Callable JumpToTabletButton => Callable.From(JumpToTablet);

	private void JumpToTablet()
	{
		if (!Engine.IsEditorHint())
		{
			return;
		}

		EditorSceneCamera.JumpToCastleTablet(this, Castle);
	}
}
