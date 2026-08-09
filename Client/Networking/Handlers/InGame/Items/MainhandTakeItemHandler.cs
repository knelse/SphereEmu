using System.Collections.Generic;
using System.Threading.Tasks;
using BitStreams;
using SphereHelpers.Extensions;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

// Taking an item into the hand and letting go of it. Weapons are bound to a hotkey rather than worn
// in a slot, and this is what the key press produces.
public class MainhandTakeItemHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    /// <summary>Frame length to the bit offset of the item id. The state follows immediately.</summary>
    private static readonly Dictionary<int, int> ItemIdBitOffsetByFrameLength = new()
    {
        [0x15] = 141, // hand was empty, a powder-class item goes in
        [0x19] = 172, // hand was empty, a sword-class item goes in
        [0x1B] = 188, // powder replaced by powder
        [0x1F] = 219, // powder and sword swapped, either direction
        [0x23] = 250, // sword replaced by sword
    };

    /// <summary>The state the hand ends in.</summary>
    private const int HandEmpty = 255;

    private const int HandFists = 22;

    private const ushort NoItem = 0xFFFF;

    public async Task Handle (byte[] frame, double delta)
    {
        // Taking an item in hand arms the same client use-lock as an attack; without the ack the
        // client wedges.
        clientConnection.MaybeScheduleNetworkPacketSend(CommonPackets.ClearUseToutAck(localId));

        var character = clientConnection.GetSelectedCharacter();
        if (character is null)
        {
            return;
        }

        var buffer = frame;
        var frameLength = buffer[0] | (buffer[1] << 8);

        if (!ItemIdBitOffsetByFrameLength.TryGetValue(frameLength, out var itemIdBit))
        {
            // A known offset read out of an unknown frame yields whatever happens to be there.
            SphLogger.Debug($"Main hand: no grammar for a {frameLength}B frame, signature " +
                            $"{buffer[13]:X2} {buffer[14]:X2} {buffer[15]:X2}. Client ID: {localId:X4}");
            return;
        }

        var stream = new BitStream(buffer);
        stream.ReadBits(itemIdBit);
        var itemId = stream.ReadUInt16(16);

        // 11 bits in the powder form and 12 in the rest; the extra bit is padding.
        var state = stream.ReadUInt16(11);

        if (state is HandEmpty or HandFists || itemId == NoItem)
        {
            if (!character.Items.Remove(BelongingSlot.MainHand))
            {
                SphLogger.Info($"Hand emptied - nothing was held. Client ID: {localId:X4}");
                return;
            }

            SphLogger.Info($"Hand emptied. Client ID: {localId:X4}");
            Persist(character);
            return;
        }

        var item = DbConnection.Items.FindById((int) itemId);
        if (item is null)
        {
            SphLogger.Warning($"Hand: no item {itemId:X4} in a {frameLength}B frame. " +
                              $"Client ID: {localId:X4}");
            return;
        }

        character.PlaceItemInSlot(BelongingSlot.MainHand, item.Id);
        SphLogger.Info($"Took {item.Localization.GetValueOrDefault(Locale.Russian, "?")} [{item.Id}] " +
                       $"in hand. Client ID: {localId:X4}");
        Persist(character);
    }

    private void Persist (CharacterDbEntry character)
    {
        if (character.RecalcCurrentStats())
        {
            NetworkedStatsUpdater.Update(character);
        }

        clientConnection.SaveSelectedCharacter();
    }
}
