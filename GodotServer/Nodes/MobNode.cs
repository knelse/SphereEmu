using Godot;

public partial class MobNode : CharacterBody3D
{
	private bool followActive;
	private Node3D? clientModel;
	private Client? client;
	private const float speed = 5.5f;
	private Vector3 lastKnownClientPosition = Vector3.Zero;
	private readonly RandomNumberGenerator rng = new();

	private double networkCoordsUpdateDelay = 0.5f;
	private double attackDelay;
	private NavigationAgent3D navigationAgent;

	public Mob Mob;
	
	public override void _Ready()
	{
		Mob.ShowForEveryClientInRadius();
		navigationAgent = GetNode<NavigationAgent3D>("NavigationAgent3D");
	}

	public override void _PhysicsProcess(double delta)
	{
		// TODO: replace with signal later
		clientModel ??= GetNodeOrNull<Node3D>("/root/MainServer/Client/ClientModel");
		client ??= GetNodeOrNull<Client>("/root/MainServer/Client");
		if ((client?.StreamPeer.GetStatus() ?? StreamPeerTCP.Status.None) != StreamPeerTCP.Status.Connected)
		{
			clientModel = null;
			client = null;
			followActive = false;
			lastKnownClientPosition = Vector3.Zero;
		}

		if (clientModel == null)
		{
			return;
		}
		
		if (!followActive && GlobalTransform.origin.DistanceTo(clientModel.GlobalTransform.origin) <= 10)
		{
			followActive = true;
			lastKnownClientPosition = clientModel.GlobalTransform.origin;
			navigationAgent.SetTargetLocation(lastKnownClientPosition);
		}

		if (!followActive)
		{
			return;
		}

		networkCoordsUpdateDelay -= delta;

		if (networkCoordsUpdateDelay <= 0)
		{
			networkCoordsUpdateDelay = 0.5f;

			client?.MoveEntity(GlobalTransform.origin.x, -GlobalTransform.origin.y + 1,
				GlobalTransform.origin.z, Mathf.Pi - Transform.basis.GetEuler().y, client.GetLocalObjectId(Mob.Id));
		}

		attackDelay -= delta;

		if (attackDelay <= 0 && GlobalTransform.origin.DistanceTo(clientModel.GlobalTransform.origin) <= 2)
		{
			client?.ChangeHealth(client.GetLocalObjectId(Mob.Id), -rng.RandiRange(5, 8));
			attackDelay = 3.5f;
		}

		if (clientModel.GlobalTransform.origin.DistanceTo(lastKnownClientPosition) >= 0.2)
		{
			lastKnownClientPosition = clientModel.GlobalTransform.origin;
			navigationAgent.SetTargetLocation(lastKnownClientPosition);
		}

		if (!followActive || navigationAgent.GetFinalLocation().DistanceTo(GlobalTransform.origin) < 0.2)
		{
			return;
		}

		var next = navigationAgent.GetNextLocation();
		var direction = GlobalTransform.origin.DirectionTo(next);

		Velocity = direction.Normalized() * speed;
		MoveAndSlide();
		LookAt(clientModel.GlobalTransform.origin, Vector3.Up);
	}

	public void SetInactive()
	{
		// TODO: remove stub
		QueueFree();
	}
}
