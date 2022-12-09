using System;
using Godot;

public partial class LootBagNode : RigidBody3D
{
	public LootBag LootBag = new ();
	public double updateDelay = 0.1f;

	public override void _Ready()
	{
		LootBag.ShowForEveryClientInRadius();
		GravityScale = 0.3f;
	}

	public override void _Process(double delta)
	{
		updateDelay -= delta;
		LootBag.X = GlobalTransform.origin.x;
		LootBag.Y = GlobalTransform.origin.y;
		LootBag.Z = GlobalTransform.origin.z;

		if (updateDelay < 0)
		{
			updateDelay = 0.1f;
			// LootBag.UpdatePositionForEveryClientInRadius();
		}
	}
}
