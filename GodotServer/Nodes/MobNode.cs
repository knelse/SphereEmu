using System;
using System.Text;
using Godot;
using SphServer;
using SphServer.DataModels;
using SphServer.Helpers;
using SphServer.Packets;

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
    public GameObjectData GameObjectData { get; set; } // unused for now

    private static readonly PackedScene MobScene = (PackedScene) ResourceLoader.Load("res://Mob.tscn");
    
    public MobNode ParentNode;    
    
    public static Mob Create(double x, double y, double z, double turn, int unknown, int level, int currentHp, int typeId)
    {
        var mob = (MobNode) MobScene.Instance();
        mob.Mob = new Mob
        {
            X = x,
            Y = y,
            Z = z,
            Turn = turn,
            ParentNode = mob,
            Unknown = (ushort) unknown,
            CurrentHP = (ushort) currentHp,
            MaxHP = (ushort) currentHp,
            TypeID = (ushort) typeId,
            TitleLevelMinusOne = (byte) level
        };
        mob.Mob.ID = MainServer.AddToGameObjects(mob.Mob);
        
        MainServer.MainServerNode.AddChild(mob);
        return mob.Mob;
    }

    public void ShowForEveryClientInRadius()
    {
        foreach (var ent in MainServer.GameObjects.Values)
        {
            // TODO: proper load/unload for client
            if (ent is CharacterData charData)
                // && charData.Client.DistanceTo(ParentNode.GlobalTransform.origin) <=
                // MainServer.CLIENT_OBJECT_VISIBILITY_DISTANCE)
            {
                Console.WriteLine($"CLI {charData.ID}: MOB {ent.ID} {ent.X} {ent.Y} {ent.Z}");
                Client.TryFindClientByIdAndSendData(charData.ID, Packet.ToByteArray(ToByteArray(), 1));
            }
        }
    }

    // TODO: unhorrify
    public byte[] ToByteArray()
    {
        var sb = new StringBuilder();
        sb.Append("11111100"); //fc
        sb.Append("11010010"); //d2
        sb.Append("00111001"); //39
        sb.Append("01110000"); //70
        sb.Append("00000000"); //00
        sb.Append("11000000"); //c0
        var id_str = ID.ToBinaryString();
        sb.Append(id_str[13..]);
        sb.Append("01111");
        sb.Append(id_str[5..13]);
        var enttype_str = Unknown.ToBinaryString();
        sb.Append("000");
        // sb.Append(enttype_str[13..]);
        sb.Append(id_str[..5]);
        sb.Append("01101001");
        sb.Append("11110000");
        // sb.Append(enttype_str[5..13]);
        // sb.Append("111");
        // sb.Append(enttype_str[..5]);
        var x_str = BitHelper.ByteArrayToBinaryString(CoordsHelper.EncodeServerCoordinate(X));
        var y_str = BitHelper.ByteArrayToBinaryString(CoordsHelper.EncodeServerCoordinate(-Y));
        var z_str = BitHelper.ByteArrayToBinaryString(CoordsHelper.EncodeServerCoordinate(Z));
        var t_str = BitHelper.ByteArrayToBinaryString(CoordsHelper.EncodeServerCoordinate(Turn));


        sb.Append(x_str[2..8]);
        sb.Append("01");
        sb.Append(x_str[10..16]);
        sb.Append(x_str[..2]);
        sb.Append(x_str[18..24]);
        sb.Append(x_str[8..10]);
        sb.Append(x_str[26..32]);
        sb.Append(x_str[16..18]);
        sb.Append(y_str[2..8]);
        sb.Append(x_str[24..26]);

        sb.Append(y_str[10..16]);
        sb.Append(y_str[..2]);
        sb.Append(y_str[18..24]);
        sb.Append(y_str[8..10]);
        sb.Append(y_str[26..32]);
        sb.Append(y_str[16..18]);
        sb.Append(z_str[2..8]);
        sb.Append(y_str[24..26]);

        sb.Append(z_str[10..16]);
        sb.Append(z_str[..2]);
        sb.Append(z_str[18..24]);
        sb.Append(z_str[8..10]);
        sb.Append(z_str[26..32]);
        sb.Append(z_str[16..18]);
        sb.Append("101011");
        sb.Append(z_str[24..26]);
        sb.Append("01000110");
        // sb.Append(t_str[6..14]);
        // sb.Append(t_str[14..22]);
        // sb.Append(t_str[22..30]);
        // var hp_str = CurrentHP.ToBinaryString();
        sb.Append("11111100");
        // sb.Append(hp_str[10..]);
        // sb.Append(t_str[30..]);
        // sb.Append(hp_str[6..14]);
        // sb.Append("10");
        // sb.Append(hp_str[..6]);
        sb.Append("10000000");
        sb.Append("11111000");
        sb.Append("00000001");
        var entid_str = TypeID.ToBinaryString();
        sb.Append(entid_str[9..]);
        sb.Append("1");
        sb.Append(entid_str[1..9]);
        // works for levels up to 128 /shrug
        var level_str = TitleLevelMinusOne.ToBinaryString();
        sb.Append(level_str[2..]);
        sb.Append("01");
        sb.Append("111101");
        sb.Append(level_str[..2]);
        sb.Append("00000001");

        return BitHelper.BinaryStringToByteArray(sb.ToString());
    }
}

public class MobNode : KinematicBody
{
    private bool followActive;
    private Spatial? clientModel;
    private Client? client;
    private const float speed = 5.5f;
    private Vector3 lastKnownClientPosition = Vector3.Zero;
    private readonly RandomNumberGenerator rng = new();

    private float networkCoordsUpdateDelay = 0.5f;
    private float attackDelay;
    private NavigationAgent navigationAgent;

    public Mob Mob;
    
    public override void _Ready()
    {
        Mob.ShowForEveryClientInRadius();
        navigationAgent = GetNode<NavigationAgent>("NavigationAgent");
    }

    public override void _PhysicsProcess(float delta)
    {
        // TODO: replace with signal later
        clientModel ??= GetNodeOrNull<Spatial>("/root/MainServer/Client/ClientModel");
        client ??= GetNodeOrNull<Client>("/root/MainServer/Client");
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
                GlobalTransform.origin.z, Mathf.Pi - Transform.basis.GetEuler().y, Mob.ID);
        }

        attackDelay -= delta;

        if (attackDelay <= 0 && GlobalTransform.origin.DistanceTo(clientModel.GlobalTransform.origin) <= 2)
        {
            client?.ChangeHealth(Mob.ID, -rng.RandiRange(5, 8));
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
        MoveAndSlide(direction.Normalized() * speed);
        LookAt(clientModel.GlobalTransform.origin, Vector3.Up);
    }

    public void SetInactive()
    {
        // TODO: remove stub
        QueueFree();
    }
}
