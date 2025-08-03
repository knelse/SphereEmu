using System.Collections.Generic;
using Godot;
using SphServer;
using SphServer.Repositories;

public partial class MobNode : CharacterBody3D
{
    private bool followActive;
    private Node3D? clientModel;
    private Client? client;
    private const float speed = 5.5f;
    private Vector3 lastKnownClientPosition = Vector3.Zero;
    private readonly RandomNumberGenerator rng = new ();

    private double networkCoordsUpdateDelay = 0.5f;
    private double attackDelay;
    private NavigationAgent3D navigationAgent;

    public Mob Mob;

    public override void _Ready ()
    {
        Mob.ShowForEveryClientInRadius();
        navigationAgent = GetNode<NavigationAgent3D>("NavigationAgent3D");
    }

    public override void _PhysicsProcess (double delta)
    {
        // TODO: replace with signal later
        clientModel ??= GetNodeOrNull<Node3D>("/root/MainServer/Client/ClientModel");
        client ??= GetNodeOrNull<Client>("/root/MainServer/Client");
        if ((client?.StreamPeer.GetStatus() ?? StreamPeerTcp.Status.None) != StreamPeerTcp.Status.Connected)
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

        if (!followActive && GlobalTransform.Origin.DistanceTo(clientModel.GlobalTransform.Origin) <= 10)
        {
            followActive = true;
            lastKnownClientPosition = clientModel.GlobalTransform.Origin;
            navigationAgent.TargetPosition = lastKnownClientPosition;
        }

        if (!followActive)
        {
            return;
        }

        networkCoordsUpdateDelay -= delta;

        if (networkCoordsUpdateDelay <= 0)
        {
            networkCoordsUpdateDelay = 0.5f;

            client?.MoveEntity(GlobalTransform.Origin.X, -GlobalTransform.Origin.Y + 1,
                GlobalTransform.Origin.Z, Mathf.Pi - Transform.Basis.GetEuler().Y, client.GetLocalObjectId(Mob.Id));
        }

        attackDelay -= delta;

        if (attackDelay <= 0 && GlobalTransform.Origin.DistanceTo(clientModel.GlobalTransform.Origin) <= 2)
        {
            client?.ChangeHealth(client.GetLocalObjectId(Mob.Id), -rng.RandiRange(5, 8));
            attackDelay = 3.5f;
        }

        if (clientModel.GlobalTransform.Origin.DistanceTo(lastKnownClientPosition) >= 0.2)
        {
            lastKnownClientPosition = clientModel.GlobalTransform.Origin;
            navigationAgent.TargetPosition = lastKnownClientPosition;
        }

        if (!followActive || navigationAgent.GetFinalPosition().DistanceTo(GlobalTransform.Origin) < 0.2)
        {
            return;
        }

        var next = navigationAgent.GetNextPathPosition();
        var direction = GlobalTransform.Origin.DirectionTo(next);

        Velocity = direction.Normalized() * speed;
        MoveAndSlide();
        LookAt(clientModel.GlobalTransform.Origin, Vector3.Up);
    }

    public void SetInactive ()
    {
        ActiveNodesRepository.Remove(GetInstanceId());
        QueueFree();
    }
}