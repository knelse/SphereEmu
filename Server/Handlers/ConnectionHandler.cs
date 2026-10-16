using Godot;
using SphServer.Client;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.WorldState;

namespace SphServer.Server.Handlers;

public class ConnectionHandler (PackedScene clientScene, Node parentNode)
{
    public SphereClient? HandleUdp (PlayerDbEntry player, string endpoint)
    {
        var client = clientScene.Instantiate<SphereClient> ();
        ActiveClients.Insert (client);
        client.SetupUdp (endpoint);
        if (!client.TryBindResume (player))
        {
            client.RemoveClient ();
            return null;
        }

        ActiveNodes.Add (client.GetInstanceId (), client);
        return client;
    }

    public void Attach (SphereClient client)
    {
        parentNode.AddChild (client);
    }
}
