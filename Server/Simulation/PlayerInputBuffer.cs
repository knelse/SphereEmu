namespace SphServer.Server.Simulation;

public sealed class PlayerInputSlot
{
	public uint LastApplied;
	public float WishX;
	public float WishZ;
	public float Yaw;
	public ushort Buttons;
	public Guid TargetId;
	public ulong LastEnqueueTick;
	public bool HasDatagram;
	public List<PlayerCommand> Queue { get; } = new List<PlayerCommand> ();
}

public static class PlayerInputBuffer
{
	public const int MaxQueuedCommands = 32;
	public const int MaxSeqAhead = 8;

	public static void ApplyLatest (PlayerInputSlot slot, ulong tick, List<PlayerCommand> commands)
	{
		if (commands.Count == 0)
		{
			return;
		}

		var newest = commands[0];
		for (var i = 1; i < commands.Count; i++)
		{
			if (commands[i].Seq >= newest.Seq)
			{
				newest = commands[i];
			}
		}

		// This datagram is the latest send. An older LastApplied must not keep the first wish.
		slot.LastApplied = newest.Seq;
		slot.WishX = newest.WishX;
		slot.WishZ = newest.WishZ;
		slot.Yaw = newest.Yaw;
		slot.Buttons = newest.Buttons;
		slot.TargetId = newest.TargetId;
		slot.LastEnqueueTick = tick;
		slot.HasDatagram = true;
		slot.Queue.Clear ();
	}

	public static bool Enqueue (PlayerInputSlot slot, ulong tick, PlayerCommand command)
	{
		var expected = slot.LastApplied + 1u;
		// Already applied, or more than 8 past the next seq
		if (command.Seq < expected || command.Seq - expected > (uint) MaxSeqAhead)
		{
			return false;
		}

		for (var i = 0; i < slot.Queue.Count; i++)
		{
			if (slot.Queue[i].Seq == command.Seq)
			{
				return false;
			}
		}

		if (slot.Queue.Count >= MaxQueuedCommands)
		{
			return false;
		}

		slot.Queue.Add (command);
		slot.LastEnqueueTick = tick;
		slot.HasDatagram = true;
		return true;
	}

	public static void Step (PlayerInputSlot slot, ulong tick)
	{
		var expected = slot.LastApplied + 1u;
		var match = -1;
		for (var i = 0; i < slot.Queue.Count; i++)
		{
			if (slot.Queue[i].Seq == expected)
			{
				match = i;
				break;
			}
		}

		if (match >= 0)
		{
			var command = slot.Queue[match];
			slot.Queue.RemoveAt (match);
			slot.LastApplied = command.Seq;
			slot.WishX = command.WishX;
			slot.WishZ = command.WishZ;
			slot.Yaw = command.Yaw;
			slot.Buttons = command.Buttons;
			slot.TargetId = command.TargetId;
			return;
		}

		// No datagram for 200 ms, so the wish and buttons stop
		if (!slot.HasDatagram || tick - slot.LastEnqueueTick >= (ulong) SimulationClock.WishTimeoutTicks)
		{
			slot.WishX = 0f;
			slot.WishZ = 0f;
			slot.Buttons = 0;
		}
	}
}
