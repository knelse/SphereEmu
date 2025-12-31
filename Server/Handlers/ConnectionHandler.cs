using Godot;
using SphServer.Client;
using SphServer.Server.Config;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;

namespace SphServer.Server.Handlers;

public class ConnectionHandler (PackedScene clientScene, Node parentNode)
{
    public void Handle (StreamPeerTcp streamPeer)
    {
        streamPeer.SetNoDelay(true);
        
        var ipAddress = streamPeer.GetConnectedHost();
        
        // Check if IP is banned
        if (BannedClients.IsIpBanned(ipAddress))
        {
            SphLogger.Info($"Rejected connection from banned IP: {ipAddress}");
            streamPeer.DisconnectFromHost();
            return;
        }
        
        var client = clientScene.Instantiate<SphereClient>();
        client.Setup(streamPeer, ActiveClients.InsertAtFirstEmptyIndex(client));
        ActiveNodes.Add(client.GetInstanceId(), client);

        parentNode.AddChild(client);
    }
}