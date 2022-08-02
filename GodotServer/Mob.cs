using System;
using Godot;

public class Mob : KinematicBody
{
    private bool followActive;
    private Spatial? clientModel;
    private Client? client;
    private const float speed = 25f;// 5.5f;
    private Vector3[] path;
    private int pathNode;
    private Navigation navMesh = null!;
    private Vector3 lastKnownClientPosition = Vector3.Zero;
    private readonly RandomNumberGenerator rng = new();

    private float networkCoordsUpdateDelay = 0.5f;
    private float attackDelay;
    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        navMesh = GetNode<Navigation>("/root/MainServer/NewPlayerDungeon/Navigation");
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(float delta)
    {
        // TODO: replace with signal later
        clientModel ??= GetNodeOrNull<Spatial>("/root/MainServer/Client/ClientModel");
        client ??= GetNodeOrNull<Client>("/root/MainServer/Client");
        // clientModel ??= GetNodeOrNull<Spatial>(
        //     "/root/MainServer/NewPlayerDungeon/Navigation/NavigationMeshInstance/NewPlayerDungeon/Room2/Podium");
        if ((client?.streamPeer.IsConnectedToHost() ?? true) == false)
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
        
        if (!followActive && Transform.origin.DistanceTo(clientModel.GlobalTransform.origin) <= 10)
        {
            followActive = true;
            lastKnownClientPosition = clientModel.GlobalTransform.origin;
            path = navMesh.GetSimplePath(Transform.origin, clientModel.GlobalTransform.origin);
        }

        if (!followActive)
        {
            return;
        }

        networkCoordsUpdateDelay -= delta;

        if (networkCoordsUpdateDelay <= 0)
        {
            networkCoordsUpdateDelay = 0.5f;

            client?.MoveEntity(GlobalTransform.origin.x, GlobalTransform.origin.y + 0.75,
                GlobalTransform.origin.z, Mathf.Pi - Transform.basis.GetEuler().y, 54321);
        }

        attackDelay -= delta;

        if (attackDelay <= 0 && GlobalTransform.origin.DistanceTo(clientModel.GlobalTransform.origin) <= 2)
        {
            client?.ChangeHealth(54321, -rng.RandiRange(5, 8));
            attackDelay = 3.5f;
        }

        if (clientModel.GlobalTransform.origin.DistanceTo(lastKnownClientPosition) >= 0.2)
        {
            path = navMesh.GetSimplePath(Transform.origin, clientModel.GlobalTransform.origin);
            lastKnownClientPosition = clientModel.GlobalTransform.origin;
            pathNode = 0;
        }

        if (!followActive || pathNode >= path.Length)
        {
            return;
        }

        var direction = path[pathNode] - GlobalTransform.origin;
        
        if (direction.Length() < 0.5)
        {
            pathNode += 1;
        }
        else
        {
            MoveAndSlide(direction.Normalized() * speed, Vector3.Up);
            LookAt(clientModel.GlobalTransform.origin, Vector3.Up);
        }
    }

    public void SetInactive()
    {
        // TODO: remove stub
        QueueFree();
        Console.WriteLine("Inactive");
    }
}
