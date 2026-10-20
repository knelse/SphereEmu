using System;
using System.Threading.Tasks;
using SphServer.Server.GameplayLogic.Groups;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;
using SphServer.System;

namespace SphServer.Client.Networking.Handlers.InGame.Communities;

/// <summary>
/// _player region 12: cmd 25 is the group, subcode first; cmd 46 invites, 50 and 51 answer an invite
/// </summary>
public class GroupActionsHandler (ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (byte[] frame, double delta)
    {
        var client = ActiveClients.Get (clientConnection.LocalId);
        if (client is null || !TryReadManagerRecord (frame, out var command, out var data))
        {
            return;
        }

        switch (command)
        {
            case 25 when data.Length > 0:
                HandleGroupCommand (client, data[0], ReadName (data, 1));
                break;
            case 46:
                PlayerGroups.Invite (client, ReadName (data, 0));
                break;
            case 50 or 51 when data.Length >= 4:
                PlayerGroups.AnswerInvite (client, BitConverter.ToUInt32 (data, 0), accepted: command == 50);
                break;
        }
    }

    private void HandleGroupCommand (SphereClient client, byte subcode, string name)
    {
        SphLogger.Info ($"Group command {subcode} {name}. Client ID: {clientConnection.LocalId:X4}");
        switch (subcode)
        {
            case 0:
                PlayerGroups.Create (client);
                break;
            case 1:
                PlayerGroups.AskToJoin (client, name);
                break;
            case 2:
                PlayerGroups.Accept (client, name);
                break;
            case 3:
                PlayerGroups.Kick (client, name);
                break;
            case 4:
                // Also sent when the leader picks himself in the list
                PlayerGroups.Disband (client);
                break;
            case 7:
                PlayerGroups.Leave (client);
                break;
        }
    }

    /// <summary>
    /// The record after the tag is u8 command, u8 count, then the data, all 5 bits off the byte grid
    /// </summary>
    private static bool TryReadManagerRecord (byte[] frame, out byte command, out byte[] data)
    {
        data = [];
        command = 0;
        if (frame.Length < 18)
        {
            return false;
        }

        command = ShiftedByte (frame, 15);
        var count = ShiftedByte (frame, 16);
        if (frame.Length < 18 + count)
        {
            return false;
        }

        data = new byte[count];
        for (var i = 0; i < count; i++)
        {
            data[i] = ShiftedByte (frame, 17 + i);
        }

        return true;
    }

    private static byte ShiftedByte (byte[] frame, int index) =>
        (byte) ((frame[index] >> 5) | ((frame[index + 1] & 0x1F) << 3));

    /// <summary>
    /// cp1251 up to the terminating zero
    /// </summary>
    private static string ReadName (byte[] data, int start)
    {
        if (start >= data.Length)
        {
            return string.Empty;
        }

        var end = Array.IndexOf (data, (byte) 0, start);
        return SphEncoding.Win1251.GetString (data, start, (end < 0 ? data.Length : end) - start);
    }
}
