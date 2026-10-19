namespace SphServer.Server.Simulation;

public struct PlayerCommand
{
	public const ushort Attack = 1;
	public const ushort Use = 2;
	public const ushort Jump = 4;

	public uint Seq;
	public float WishX;
	public float WishZ;
	public float Yaw;
	public ushort Buttons;

	// Empty when the command has no target
	public Guid TargetId;
}
