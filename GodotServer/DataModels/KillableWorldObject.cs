namespace SphServer.DataModels;

public abstract class KillableWorldObject : WorldObject
{
    public ushort CurrentHP { get; set; }
    public ushort MaxHP { get; set; }
    public ushort PDef { get; set; }
    public ushort MDef { get; set; }
    public KarmaTier Karma { get; set; } = KarmaTier.Neutral;
}