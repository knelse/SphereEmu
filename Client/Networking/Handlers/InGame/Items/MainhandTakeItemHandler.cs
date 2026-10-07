using System.Threading.Tasks;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Helpers.Networking;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

// Weapons sit on a hotkey, not a worn slot, and the key press is this frame
public class MainhandTakeItemHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (byte[] frame, double delta)
    {
        // Taking an item in hand arms g_6008, so this ack clears it or the client wedges
        clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.ClearUseToutAck (localId));

        var character = clientConnection.GetSelectedCharacter ();
        if (character is null)
        {
            return;
        }

        if (!MainhandFrame.TryRead (frame, out var take))
        {
            var frameLength = frame[0] | (frame[1] << 8);
            SphLogger.Debug ($"Main hand: no grammar for a {frameLength}B frame, signature " +
                            $"{frame[13]:X2} {frame[14]:X2} {frame[15]:X2}. Client ID: {localId:X4}");
            return;
        }

        if (take.IsUnequip)
        {
            if (!character.Items.Remove (BelongingSlot.MainHand))
            {
                SphLogger.Info ($"Hand emptied - nothing was held. Client ID: {localId:X4}");
                return;
            }

            SphLogger.Info ($"Hand emptied ({take.State}). Client ID: {localId:X4}");
            Persist (character);
            return;
        }

        var item = DbConnection.Items.FindById ((int) take.ItemId);
        if (item is null)
        {
            SphLogger.Warning ($"Hand: no item {take.ItemId:X4} in a {take.FrameLength}B frame. " +
                              $"Client ID: {localId:X4}");
            return;
        }

        character.PlaceItemInSlot (BelongingSlot.MainHand, item.Id);
        SphLogger.Info ($"Took {item.Localization.GetValueOrDefault (Locale.Russian, "?")} [{item.Id}] " +
                       $"in hand. Client ID: {localId:X4}");
        Persist (character);
    }

    private void Persist (CharacterDbEntry character)
    {
        if (character.RecalcCurrentStats ())
        {
            NetworkedStatsUpdater.Update (character);
        }

        clientConnection.SaveSelectedCharacter ();
    }
}
