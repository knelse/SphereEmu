using Godot;
using SphServer;

public class Mob : IGameEntity
{
    public ushort ID { get; set; }
    public ushort Unknown { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Turn { get; set; }
    public ushort CurrentHP { get; set; }
    public ushort MaxHP { get; set; }
    public ushort TypeID { get; set; }
    public byte TitleLevelMinusOne { get; set; }
    public byte DegreeLevelMinusOne { get; set; }

    private static readonly PackedScene MobScene = (PackedScene) ResourceLoader.Load("res://Mob.tscn");
    
    public MobNode ParentNode;    
    
    public static Mob Create(double x, double y, double z, int level, int sourceTypeId)
    {
        var mob = (MobNode) MobScene.Instance();
        mob.Mob = new Mob();
        mob.Mob.ID = MainServer.AddToGameObjects(mob.Mob);
        mob.Mob.X = x;
        mob.Mob.Y = y;
        mob.Mob.Z = z;
        mob.Mob.ParentNode = mob;
        
        MainServer.MainServerNode.AddChild(mob);
        return mob.Mob;
    }
}

public class MobNode : KinematicBody
{
    private bool followActive;
    private Spatial? clientModel;
    private Client? client;
    private const float speed = 5.5f;
    private Vector3[] path;
    private int pathNode;
    private Navigation navMesh = null!;
    private Vector3 lastKnownClientPosition = Vector3.Zero;
    private readonly RandomNumberGenerator rng = new();

    private float networkCoordsUpdateDelay = 0.5f;
    private float attackDelay;

    public Mob Mob;
    
    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        navMesh = GetNode<Navigation>("/root/MainServer/NewPlayerDungeon/Navigation");
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(float delta)
    {
        // TODO: replace with signal later
        clientModel ??= GetNodeOrNull<Spatial>("/root/MainServer/ClientScene/ClientModel");
        client ??= GetNodeOrNull<Client>("/root/MainServer/ClientScene");
        // clientModel ??= GetNodeOrNull<Spatial>(
        //     "/root/MainServer/NewPlayerDungeon/Navigation/NavigationMeshInstance/NewPlayerDungeon/Room2/Podium");
        if ((client?.StreamPeer.IsConnectedToHost() ?? true) == false)
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
            client?.ChangeHealth(ID, -rng.RandiRange(5, 8));
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
    }

    public ushort ID { get; set; }
}
