using Godot;
using System;
using System.Collections.Concurrent;
using SphServer.Providers;
using SphServer.Repositories;

namespace SphServer.Server.Handlers;

public class ConnectionHandler (PackedScene clientScene, Node parentNode)
{
    public void Handle (StreamPeerTcp streamPeer)
    {
        streamPeer.SetNoDelay(true);
        var client = clientScene.Instantiate<Client>();
        client.StreamPeer = streamPeer;
        client.LocalId = ActiveClientsRepository.InsertAtFirstEmptyIndex(client);
        ActiveNodesRepository.Set(client.GetInstanceId(), client);

        SphLogger.Info($"New client connected. Client ID: {client.LocalId}");

        parentNode.AddChild(client);
    }
}