using Godot;

namespace SphServer.Shared.Db.DataModels;

public class MonsterDbEntry
{
    private static readonly PackedScene MonsterScene =
        (PackedScene) ResourceLoader.Load("res://Godot/Scenes/Monster.tscn");

    public int Id { get; set; }
    public ushort TypeID { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Angle { get; set; }
    public SphMonsterInstance Monster { get; set; }
    public ulong? ParentNodeId { get; set; }
}