using Godot;

namespace SphServer.Addons.MonsterSpawnerGizmo;

[Tool]
public partial class MonsterSpawnerEditorPlugin : EditorPlugin
{
	private MonsterSpawnerGizmoPlugin? _gizmoPlugin;

	public override void _EnterTree()
	{
		_gizmoPlugin = new MonsterSpawnerGizmoPlugin();
		AddNode3DGizmoPlugin(_gizmoPlugin);
	}

	public override void _ExitTree()
	{
		if (_gizmoPlugin is not null)
		{
			RemoveNode3DGizmoPlugin(_gizmoPlugin);
			_gizmoPlugin = null;
		}
	}
}
