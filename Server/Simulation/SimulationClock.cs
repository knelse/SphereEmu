namespace SphServer.Server.Simulation;

public static class SimulationClock
{
	public const int TickHz = 60;

	public const double SecondsPerTick = 1.0 / TickHz;

	// 200 ms at 60 Hz
	public const int WishTimeoutTicks = 12;

	public static void Advance (ref ulong tick, ref double simulatedTime)
	{
		tick += 1;
		simulatedTime += SecondsPerTick;
	}
}
