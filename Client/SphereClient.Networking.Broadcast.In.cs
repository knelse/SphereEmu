namespace SphServer.Client;

public partial class SphereClient
{
	private ClientEvents clientEvents = null!;

	public void EnqueueClientEvent (ClientQueuedEvent clientEvent)
	{
		clientEvents.Enqueue (clientEvent);
	}

	private async Task DrainIncomingEvents ()
	{
		await clientEvents.HandleEventsAsync ();
	}

	public void MaybeQueueNetworkPacketSend (byte[] packet)
	{
		if (IsAdminDebugDummy)
		{
			return;
		}

		clientConnection.MaybeScheduleNetworkPacketSend (packet);
	}

	/// <summary>
	/// Enqueue only, so HP and the health packets stay in one place
	/// </summary>
	public void BroadcastApplyHpDelta (int hpDelta)
	{
		if (CurrentCharacter is null || hpDelta == 0)
		{
			return;
		}

		EnqueueClientEvent (new CharacterHealthChangeEvent (CurrentCharacter.EntityId, hpDelta));
	}
}
