using System;
using Godot;

public class LootBagNode : RigidBody
{
    public LootBag LootBag = new ();
    public float updateDelay = 0.1f;

    public override void _Ready()
    {
        LootBag.ShowForEveryClientInRadius();
        GravityScale = 0.3f;
    }

    public override void _Process(float delta)
    {
        updateDelay -= delta;
        LootBag.X = GlobalTransform.origin.x;
        LootBag.Y = GlobalTransform.origin.y;
        LootBag.Z = GlobalTransform.origin.z;

        if (updateDelay < 0)
        {
            updateDelay = 0.1f;
            LootBag.UpdatePositionForEveryClientInRadius();
        }
    }
}