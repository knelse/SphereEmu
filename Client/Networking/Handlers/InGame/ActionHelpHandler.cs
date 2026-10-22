using SphServer.Helpers.Networking;
using SphServer.Shared.BitStream;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;

namespace SphServer.Client.Networking.Handlers.InGame;

/// <summary>
/// A banker, trader, guide and other NPCs ask _player.CheckActionHelp(n) on use; region 12 cmd 41
/// with the same u32 makes ShowActionHelp open language\1stusagehelp%02u.hts. The pages are first
/// usage tips, so each one is shown to a character once
/// </summary>
public static class ActionHelpHandler
{
    private const byte ShowActionHelpCommand = 41;

    /// <summary>
    /// The client ships 1stusagehelp00..17
    /// </summary>
    private const uint HelpPageCount = 18;

    public static void Handle (byte[] frame, SphereClient client)
    {
        var character = client.CurrentCharacter;
        var page = GameplayRecord.ManagerPayloadUInt32 (frame);
        if (character is null || page >= HelpPageCount)
        {
            return;
        }

        var bit = 1u << (int) page;
        if ((character.ShownActionHelp & bit) != 0)
        {
            return;
        }

        character.ShownActionHelp |= bit;
        character.PersistAll ();
        client.MaybeQueueNetworkPacketSend (
            CommonPackets.BuildPlayerManagerCommand (SphBitStream.ByteSwap (character.ClientIndex),
                ShowActionHelpCommand,
                [(byte) page, (byte) (page >> 8), (byte) (page >> 16), (byte) (page >> 24)]));
        SphLogger.Info ($"Action help {page} shown to {character.Name}. Client ID: {client.localId:X4}");
    }
}
