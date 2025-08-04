using Godot;
using SphServer.Client.Networking;
using SphServer.Client.State;
using SphServer.DataModels;
using SphServer.Providers;
using SphServer.Shared.WorldState;

namespace SphServer.Client;

public partial class SphereClient : Node
{
    private ClientState currentState = ClientState.I_AM_BREAD;

    private StaticBody3D? clientModel;

    // public Character? CurrentCharacter;
    private ClientConnection clientConnection;
    public readonly ClientStateManager ClientStateManager = new (false);
    private ushort localId;
    private StreamPeerTcp streamPeerTcp = null!;
    private PlayerDbEntry? playerDbEntry;
    private bool isExiting;

    public override async void _Process (double delta)
    {
        if (streamPeerTcp.GetStatus() != StreamPeerTcp.Status.Connected)
        {
            RemoveClient();
            return;
        }

        clientModel ??= GetNode<StaticBody3D>("ClientModel");

        clientConnection.Process(delta);
    }

    public override void _Ready ()
    {
        // TODO: client logs in separate files
        SphLogger.Info($"New client connected. Client ID: {localId}");
        // Task.Run(() =>
        // {
        //     while (true)
        //     {
        //         var input = Console.ReadLine();
        //         try
        //         {
        //             if (CurrentCharacter is null)
        //             {
        //                 Console.WriteLine("Character is null");
        //                 continue;
        //             }
        //
        //             var parser = ConsoleCommandParser.Get(CurrentCharacter);
        //             var result = parser.Parse(input);
        //         }
        //         catch (Exception ex)
        //         {
        //             Console.WriteLine(ex.Message);
        //         }
        //     }
        // });
    }

    public SphereClient Setup (StreamPeerTcp streamPeerTcp, ushort localId)
    {
        clientConnection = new ClientConnection(streamPeerTcp, localId, this);
        this.localId = localId;
        this.streamPeerTcp = streamPeerTcp;

        return this;
    }

    public void SetPlayerDbEntry (PlayerDbEntry? entry)
    {
        playerDbEntry = entry;
    }

    public void RemoveClient ()
    {
        if (isExiting)
        {
            return;
        }

        SphLogger.Info($"Client disconnected. Client ID: {localId}");
        
        isExiting = true;
        // TODO: sync state
        clientConnection.Close();
        ActiveClients.Remove(localId);
        ActiveClients.Remove(localId);
        QueueFree();
    }
}