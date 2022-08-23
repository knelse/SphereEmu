using System;
using Godot;

public class LootBagNode : RigidBody
{
    public LootBag LootBag = new ();
    public float updateDelay = 0.1f;
    private float baseY;

    public override void _Ready()
    {
        LootBag.ShowForEveryClientInRadius();
        baseY = GlobalTransform.origin.y;
        GravityScale = 0.3f;
    }

    public override void _Process(float delta)
    {
        // Y axis direction is the opposite
        var yDiff = baseY - GlobalTransform.origin.y;
        updateDelay -= delta;
        LootBag.X = GlobalTransform.origin.x;
        LootBag.Y = baseY + yDiff;
        LootBag.Z = GlobalTransform.origin.z;

        if (updateDelay < 0)
        {
            updateDelay = 0.1f;
            LootBag.UpdatePositionForEveryClientInRadius();
        }
    }
}