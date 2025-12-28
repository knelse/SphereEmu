using System.Linq;
using SphServer.Client.Networking;
using SphServer.Shared.Networking.Chat.Encoders;
using SphServer.Shared.WorldState;

namespace SphServer.Server.Broadcast;

public static class ChatBroadcast
{
    public static void MaybeScheduleBroadcastToClients (string fullMessage, string actualText, string characterName,
        int chatTypeVal,
        ClientConnection? clientOriginConnection = null)
    {
        // TODO logging for broadcast targets
        var encodedMessage = MessageEncoder.EncodeToSendFromServer(fullMessage, characterName, chatTypeVal);

        if (actualText.StartsWith('/'))
        {
            return;
        }

        var originDbEntry = clientOriginConnection?.GetSelectedCharacter();

        if (originDbEntry is null)
        {
            // assume max range
            foreach (var sphereClient in ActiveClients.GetAll().Values.ToList())
            {
                sphereClient.MaybeQueueNetworkPacketSend(encodedMessage);
            }

            return;
        }

        var origin = originDbEntry.Origin;
        var maxRange = GetMaxChatRange(chatTypeVal);

        foreach (var sphereClient in ActiveClients.GetAll().Values.ToList())
        {
            var dbEntry = sphereClient.CurrentCharacter;
            if (dbEntry is null)
            {
                continue;
            }

            var targetClientOrigin = sphereClient.CurrentCharacter!.Origin;
            var distance = targetClientOrigin.DistanceTo(origin);
            if (distance <= maxRange)
            {
                sphereClient.MaybeQueueNetworkPacketSend(encodedMessage);
            }
        }
    }

    public static int GetMaxChatRange (int chatTypeVal)
    {
        return chatTypeVal switch
        {
            0 => 5,
            1 => 5,
            2 => 30,
            _ => int.MaxValue
        };
    }
}