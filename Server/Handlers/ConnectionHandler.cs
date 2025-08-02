using Godot;
using System;
using System.Collections.Concurrent;
using SphServer.Providers;

namespace SphServer.Server.Handlers;

public static class ConnectionHandler
{
    public static void HandleNewConnection(TcpServer tcpServer, PackedScene clientScene,
        ConcurrentDictionary<ushort, Client> activeClients,
        ConcurrentDictionary<ulong, Node> activeNodes,
        Node parentNode, ref int playerCount)
    {
        var streamPeer = tcpServer.TakeConnection();
        streamPeer.SetNoDelay(true);
        var client = clientScene.Instantiate<Client>();
        playerCount += 1;
        client.StreamPeer = streamPeer;
        client.LocalId = GetNewPlayerIndex(activeClients);
        activeClients[client.LocalId] = client;
        activeNodes[client.GetInstanceId()] = client;
        parentNode.AddChild(client);

        SphLogger.Info($"New client connected. Player count: {playerCount}, Client ID: {client.LocalId}");
    }

    private static ushort GetNewPlayerIndex(ConcurrentDictionary<ushort, Client> activeClients)
    {
        for (ushort i = 1; i < ushort.MaxValue; i++)
        {
            if (!activeClients.ContainsKey(i))
            {
                return i;
            }
        }
        throw new ArgumentException("Reached max number of connections");
    }
}