using Godot;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Client.State;
using SphServer.Server.Broadcast;
using SphServer.Server.Debug.Parser;
using SphServer.Shared.Db;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Client;

public partial class SphereClient : WorldObject
{
	public readonly ClientStateManager ClientStateManager = new (false);
	private bool isExiting;

	/// <summary>
	/// Admin UI stand-in with no session socket
	/// </summary>
	public bool IsAdminDebugDummy { get; private set; }

	public override async void _PhysicsProcess (double delta)
	{
		if (IsAdminDebugDummy)
		{
			return;
		}

		EnsureClientModel ();
		EnsureBroadcastArea ();
		await TickConnection (delta);
		await DrainIncomingEvents ();
	}

	public override void _Ready ()
	{
		SphLogger.Info ($"New client connected. Client ID: {EntityID}");
	}

	public void RemoveClient ()
	{
		if (isExiting)
		{
			return;
		}

		SphLogger.Info ($"Client disconnected. Client ID: {EntityID}");

		isExiting = true;
		DropSession ();
		SaveCharacter ();
		DbConnection.Checkpoint ();

		ActiveClients.Remove (EntityID);
		NetworkedStatsUpdater.Clear (EntityID);
		ConsoleCommandParser.Invalidate (EntityID);
		ActiveNodes.Remove (GetInstanceId ());

		if (playerDbEntry is not null)
		{
			ActiveWorldObjects.LoggedInClients.Remove (playerDbEntry.Login, out _);
		}

		PlayerCountBroadcast.OnClientLeftWorld ();

		QueueFree ();
	}
}
