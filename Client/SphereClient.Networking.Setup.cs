using SphServer.Client.Networking;
using SphServer.Server.Login.Secure;
using SphServer.Server.Simulation;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;

namespace SphServer.Client;

public partial class SphereClient
{
	private ClientConnection clientConnection = null!;
	private string udpEndpoint = "";

	public SphereClient SetupUdp (string endpoint)
	{
		clientEvents = new ClientEvents (this);
		clientConnection = new ClientConnection (this);
		udpEndpoint = endpoint;
		return this;
	}

	public void BindBeforeGameUdp (Action<byte[]> send)
	{
		clientConnection.BindBeforeGameUdp (send);
	}

	public void FeedBeforeGameUdp (byte[] frame)
	{
		clientConnection.FeedBeforeGame (frame);
	}

	public bool TryAcceptChannelDatagram (byte[] plain)
	{
		return clientConnection.TryAcceptChannelDatagram (plain);
	}

	public void SendSessionPacket (byte[] packet)
	{
		if (clientConnection is null)
		{
			return;
		}

		clientConnection.SendSessionPacket (packet);
	}

	public void SendReliable (ConnectionCommand command, ReadOnlySpan<byte> payload)
	{
		clientConnection?.SendReliable (command, payload);
	}

	public bool TryBindResume (PlayerDbEntry player)
	{
		if (!ActiveWorldObjects.LoggedInClients.TryAdd (player.Login, 1))
		{
			SphLogger.Error ($"SRV {EntityID}: Already logged in. Login: {player.Login}");
			return false;
		}

		player.Index = EntityID;
		SetPlayerDbEntry (player);
		clientConnection.ArmResume ();
		return true;
	}

	/// <summary>
	/// No session socket, for admin UI debugging. Pair with <c>AdminDebugDummyClient</c>
	/// </summary>
	public SphereClient SetupAdminDebugDummy ()
	{
		IsAdminDebugDummy = true;
		clientEvents = new ClientEvents (this);
		return this;
	}

	private async Task TickConnection (double delta)
	{
		await clientConnection.Process (delta);
	}

	private void DropSession ()
	{
		SecureLoginServer.Active?.DropClient (this);
		if (!IsAdminDebugDummy)
		{
			clientConnection.Close ();
		}
	}

	public string GetIpAddressAndPort ()
	{
		if (IsAdminDebugDummy)
		{
			return "debug:0";
		}

		return udpEndpoint.Length == 0 ? "udp:0" : udpEndpoint;
	}

	public string GetIpAddressWithoutPort ()
	{
		if (IsAdminDebugDummy)
		{
			return "debug";
		}

		var colon = udpEndpoint.LastIndexOf (':');
		return colon > 0 ? udpEndpoint[..colon] : udpEndpoint;
	}

	public Guid GetLocalObjectId (Guid id) => id;

	public Guid GetGlobalObjectId (Guid id) => id;
}
