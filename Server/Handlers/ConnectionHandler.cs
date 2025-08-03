using Godot;
using SphServer.Client;
using SphServer.Shared.WorldState;

namespace SphServer.Server.Handlers;

public class ConnectionHandler (PackedScene clientScene, Node parentNode)
{
    public void Handle (StreamPeerTcp streamPeer)
    {
        streamPeer.SetNoDelay(true);
        var client = clientScene.Instantiate<SphereClient>();
        client.Setup(streamPeer, ActiveClients.InsertAtFirstEmptyIndex(client));
        ActiveNodes.Add(client.GetInstanceId(), client);

        parentNode.AddChild(client);
    }
}