using System.Text;
using Godot;
using SphServer;
using SphServer.Helpers;

public class Mob
{
    private static readonly PackedScene MobScene = (PackedScene) ResourceLoader.Load("res://Mob.tscn");
    public int Id { get; set; }
    public ushort Unknown { get; set; }

    public ushort TypeID { get; set; }

    // TODO: filled when mobs are actual mobs
    public string ModelName { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Angle { get; set; }
    public int TitleMinusOne { get; set; }
    public int DegreeMinusOne { get; set; }
    public ushort CurrentHP { get; set; }
    public ushort MaxHP { get; set; }
    public ushort PDef { get; set; }
    public ushort MDef { get; set; }
    public KarmaTier Karma { get; set; }

    public ulong? ParentNodeId { get; set; }

    public static Mob Create (double x, double y, double z, double turn, int unknown, int level, int currentHp,
        int typeId)
    {
        var mob = MobScene.Instantiate<MobNode>();
        mob.Mob = new Mob
        {
            X = x,
            Y = y,
            Z = z,
            Angle = turn,
            ParentNodeId = mob.GetInstanceId(),
            Unknown = (ushort) unknown,
            CurrentHP = (ushort) currentHp,
            MaxHP = (ushort) currentHp,
            TypeID = (ushort) typeId,
            TitleMinusOne = (byte) level
        };
        // TODO: fix when entity spawn is done properly
        var index = (int) Client.GetNewEntityIndex();
        mob.Mob.Id = index;
        MainServer.MonsterCollection.Insert(index, mob.Mob);
        MainServer.ActiveNodes[mob.GetInstanceId()] = mob;

        MainServer.MainServerNode.AddChild(mob);
        return mob.Mob;
    }

    public void ShowForEveryClientInRadius ()
    {
        foreach (var client in MainServer.ActiveClients.Values)
        {
            // TODO: proper load/unload for client
            // && charData.Client.DistanceTo(ParentNode.GlobalTransform.origin) <=
            // MainServer.CLIENT_OBJECT_VISIBILITY_DISTANCE)
            // client.StreamPeer.PutData(Packet.ToByteArray(ToByteArray(client.LocalId), 1));
        }
    }

    // TODO: unhorrify
    public byte[] ToByteArray (ushort clientGlobalId)
    {
        var clientLocalId = Client.GetLocalObjectId(clientGlobalId, Id);
        var sb = new StringBuilder();
        sb.Append("11111100"); //fc
        sb.Append("11010010"); //d2
        sb.Append("00111001"); //39
        sb.Append("01110000"); //70
        sb.Append("00000000"); //00
        sb.Append("11000000"); //c0
        var id_str = clientLocalId.ToBinaryString();
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
        var x_str = StringConvertHelpers.ByteArrayToBinaryString(CoordsHelper.EncodeServerCoordinate(X));
        var y_str = StringConvertHelpers.ByteArrayToBinaryString(CoordsHelper.EncodeServerCoordinate(-Y));
        var z_str = StringConvertHelpers.ByteArrayToBinaryString(CoordsHelper.EncodeServerCoordinate(Z));
        var t_str = StringConvertHelpers.ByteArrayToBinaryString(CoordsHelper.EncodeServerCoordinate(Angle));


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
        var level_str = ((ushort) TitleMinusOne).ToBinaryString();
        sb.Append(level_str[2..]);
        sb.Append("01");
        sb.Append("111101");
        sb.Append(level_str[..2]);
        sb.Append("00000001");

        return BitHelper.BinaryStringToByteArray(sb.ToString());
    }
}