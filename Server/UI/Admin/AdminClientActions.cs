using SphServer.Helpers;
using SphServer.Server.Config;
using SphServer.Shared.Networking.DataModel.Serializers;
using SphServer.Shared.WorldState;

namespace SphServer.Server.UI.Admin;

/// <summary>
///     Shared kick / ban / teleport implementations for admin UI.
/// </summary>
public static class AdminClientActions
{
    public static bool Kick(ushort clientId)
    {
        var client = ActiveClients.Get(clientId);
        if (client is null)
        {
            return false;
        }

        AdminActionLog.Info(client, "kicked");
        client.RemoveClient();
        return true;
    }

    public static bool Ban(ushort clientId)
    {
        var client = ActiveClients.Get(clientId);
        if (client is null)
        {
            return false;
        }

        var login = client.GetLogin();
        var ipAddress = client.GetIpAddressWithoutPort();
        if (string.IsNullOrEmpty(login))
        {
            AdminActionLog.Warning(client, "ban failed (login is null)");
            return false;
        }

        AdminActionLog.Info(client, $"banned (login {login})");
        BannedClients.BanClient(login, ipAddress);
        client.RemoveClient();
        return true;
    }

    public static bool Teleport(ushort clientId, WorldCoords worldCoords, string destinationLabel)
    {
        var client = ActiveClients.Get(clientId);
        if (client is null || client.CurrentCharacter is null)
        {
            return false;
        }

        AdminActionLog.Info(client, $"teleported to [{worldCoords}] via \"{destinationLabel}\"");
        var teleportPacket =
            new CharacterDbEntrySerializer(client.CurrentCharacter).GetTeleportByteArray(worldCoords);
        client.MaybeQueueNetworkPacketSend(teleportPacket);
        return true;
    }
}
