using Godot;

public partial class LootBagNode : RigidBody3D
{
	public ItemContainer ItemContainer = new ();
	public double updateDelay = 0.1f;

	public override void _Ready()
	{
		ItemContainer.ShowForEveryClientInRadius();
		GravityScale = 0.3f;
	}

	public override void _Process(double delta)
	{
		updateDelay -= delta;
		ItemContainer.X = GlobalTransform.origin.x;
		ItemContainer.Y = GlobalTransform.origin.y;
		ItemContainer.Z = GlobalTransform.origin.z;

		if (updateDelay < 0)
		{
			updateDelay = 0.1f;
			// LootBag.UpdatePositionForEveryClientInRadius();
		}
	}
}
