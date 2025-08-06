// TODO: old, would probably be refactored/removed

using Godot;
using SphServer.Shared.Db.DataModels;

namespace SphServer.Godot.Nodes;

public partial class LootBagNode : RigidBody3D
{
    public ItemContainerDbEntry ItemContainerDbEntry = new ();
    public double updateDelay = 0.1f;

    public override void _Ready ()
    {
        ItemContainerDbEntry.ShowForEveryClientInRadius();
        GravityScale = 0.3f;
    }

    public override void _Process (double delta)
    {
        updateDelay -= delta;
        ItemContainerDbEntry.X = GlobalTransform.Origin.X;
        ItemContainerDbEntry.Y = GlobalTransform.Origin.Y;
        ItemContainerDbEntry.Z = GlobalTransform.Origin.Z;

        if (updateDelay < 0)
        {
            updateDelay = 0.1f;
            // LootBag.UpdatePositionForEveryClientInRadius();
        }
    }
}