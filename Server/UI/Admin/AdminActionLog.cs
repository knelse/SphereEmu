using SphServer.Client;
using SphServer.Shared.Logger;

namespace SphServer.Server.UI.Admin;

/// <summary>
///     Consistent logging for admin UI actions: player id, IP, character name, and what happened.
/// </summary>
public static class AdminActionLog
{
    public static void Info(SphereClient client, string action)
    {
        SphLogger.Info(Format(client, action));
    }

    public static void Warning(SphereClient client, string action)
    {
        SphLogger.Warning(Format(client, action));
    }

    public static void Info(ushort clientId, string? ip, string? characterName, string action)
    {
        SphLogger.Info(Format(clientId, ip, characterName, action));
    }

    private static string Format(SphereClient client, string action)
    {
        return Format(
            client.localId,
            client.GetIpAddressAndPort(),
            client.CurrentCharacter?.Name,
            action);
    }

    private static string Format(ushort clientId, string? ip, string? characterName, string action)
    {
        var name = string.IsNullOrEmpty(characterName) ? "<no character>" : characterName;
        var address = string.IsNullOrEmpty(ip) ? "<unknown>" : ip;
        return $"Admin action: player {clientId:X4}, IP {address}, char \"{name}\" — {action}";
    }
}
