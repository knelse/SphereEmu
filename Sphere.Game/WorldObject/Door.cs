using Godot;
using SphServer.Packets;
using SphServer.Shared.GameData.Enums;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class Door : WorldObject
{
    [Export] public int DoorID { get; set; }
    [Export] public bool HasTarget { get; set; }
    [Export] public double TargetX { get; set; }
    [Export] public double TargetY { get; set; }
    [Export] public double TargetZ { get; set; }

    /// <summary>
    /// Other door's guid. Jump uses this when it is set
    /// </summary>
    [Export] public string TargetEntityId { get; set; } = "";

    [ExportToolButton ("Jump to target")]
    public Callable JumpToTargetButton => Callable.From (JumpToTarget);
}
