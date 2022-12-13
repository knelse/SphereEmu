namespace SphServer.DataModels;

public abstract class WorldObject : SphGameObject
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Angle { get; set; }
}