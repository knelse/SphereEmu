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

	// char23's authored origin puts the feet 0.95 under the node, the pose the box is built on
	private const string PlayerModelName = "char23";

	// char23.glb's lowest vertex is 0.9484 under the root, past the 0.904 box base, and GroundBox
	// settles to within NearSupportMeters, so the player pose goes to the client this far higher in
	// Godot Y, and the Client.tscn Glb lifts by the same. The swept box stays on the floor line
	public const float VisualLiftMeters = 0.065f;

	/// <summary>
	/// Player Y for the classic game frame, where a send writes -godotY: the lift rides on that
	/// negation, so it subtracts
	/// </summary>
	public static double ClientGameY (double godotY) => -godotY - VisualLiftMeters;

	/// <summary>
	/// Player Y for channel 2, which writes Godot Y as written and is negated in ClientApplication
	/// </summary>
	public static float ClientGodotY (float godotY) => godotY + VisualLiftMeters;

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

	/// <summary>
	/// ObjectType Player maps to no model, so the fallback would be the placeholder pump1
	/// </summary>
	protected override string ResolveModelNameFromObjectTypeFallback () => PlayerModelName;

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
