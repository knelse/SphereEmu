using Godot;
using SphServer.Godot.Scripts.Objects;
using SphServer.Helpers;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class CastleEntrance : WorldObject
{
    public CastleEntrance ()
    {
        ObjectType = ObjectType.Castle_Entrance;
        ModelName = "edoor";
    }

    [Export] public Castles Castle { get; set; }

    /// <summary>
    /// Tablet guid. The jump button looks this up, it does not search by castle
    /// </summary>
    [Export] public string TargetEntityId { get; set; } = "";

    [ExportToolButton ("Jump to tablet")]
    public Callable JumpToTabletButton => Callable.From (JumpToTablet);

    private void JumpToTablet ()
    {
        if (!Engine.IsEditorHint ())
        {
            return;
        }

        if (!Guid.TryParse (TargetEntityId, out var tabletId))
        {
            GD.PushWarning ($"{Name}: no tablet guid.");
            return;
        }

        EditorSceneCamera.TryJumpToEntity (this, tabletId);
    }
}
