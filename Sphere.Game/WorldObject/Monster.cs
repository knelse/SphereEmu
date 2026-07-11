using Godot;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class Monster : WorldObject
{
	public required SphMonsterInstance? MonsterInstance { get; set; }

	public override void _Ready()
	{
		// Server MultiMesh visuals must refresh when spawn placement sets GlobalPosition after AddChild.
		SetNotifyTransform(true);

		if (MonsterInstance is null)
		{
			RefreshMonsterInstanceFromType();
		}
		else
		{
			SyncExportedMonsterFields();
		}

		base._Ready();

		if (Engine.IsEditorHint())
		{
			var tree = GetTree();
			if (tree is not null)
			{
				MonsterMultiMeshVisuals.RequestEditorRebuild(tree);
			}
		}
	}

	public override void _ExitTree()
	{
		if (Engine.IsEditorHint() && MonsterMultiMeshVisuals.IsBulkEditorUpdate)
		{
			MonsterMultiMeshVisuals.ForgetMonster(this);
		}
		else
		{
			MonsterMultiMeshVisuals.Unregister(this);
		}

		base._ExitTree();
	}

	public override void _Notification(int what)
	{
		base._Notification(what);
		if (what == NotificationTransformChanged && !MonsterMultiMeshVisuals.IsBulkEditorUpdate)
		{
			MonsterMultiMeshVisuals.UpdateTransformIfRegistered(this);
		}
	}
}
