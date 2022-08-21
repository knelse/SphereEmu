using Godot;

public class LootBagNode : RigidBody
{
    public LootBag LootBag;
    public float updateDelay = 0.1f;

    public override void _Ready()
    {
        var globalTransform = GlobalTransform;
        globalTransform.origin.x = (float) LootBag.X;
        globalTransform.origin.y = (float) LootBag.Y;
        globalTransform.origin.z = (float) LootBag.Z;

        GlobalTransform = globalTransform;
        
        LootBag.ShowForEveryClientInRadius();
    }

    public override void _Process(float delta)
    {
        updateDelay -= delta;

        if (updateDelay < 0)
        {
            updateDelay = 0.1f;
            LootBag.UpdatePositionForEveryClientInRadius();
        }
    }
}