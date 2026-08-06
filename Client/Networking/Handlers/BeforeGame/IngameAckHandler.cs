using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SphServer.Packets;
using SphServer.Shared.Db;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.System;
using static SphServer.Shared.BitStream.SphBitStream;

namespace SphServer.Client.Networking.Handlers.BeforeGame;

public class IngameAckHandler(ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    private SphereTimer? WaitForClientTimer;

    public async Task Handle(double delta)
    {
        if (WaitForClientTimer is not null)
        {
            WaitForClientTimer.Tick(delta);
            return;
        }

        if (clientConnection.GetIncomingData() != 0x13)
        {
            return;
        }

        var character = clientConnection.GetSelectedCharacter();

        if (character is null)
        {
            // should never happen
            SphLogger.Error($"SRV {localId:X4}: Selected character is null");
            return;
        }

        SphLogger.Info($"SRV {localId:X4}: Sending game world data");

        var worldData = CommonPackets.NewCharacterWorldData(character.ClientIndex);
        clientConnection.SendPacket(worldData[0]);
        clientConnection.SendPacket(Convert.FromHexString(
            $"BA002C01000000{localId:X4}08C002D07911C8BD10445E0C222F08C91685C80B03581CC002011609B05080C5022C1860D1000B07593CC802021611B09080C5042C286051010B0B585CC00213799189BCD0445E6CC08203161DB0F080C5072C406011020B11588CC882441625B03081C5892D506091020B1558AC422C5870D1820B1758CCD082061635B0B0C1C603848F1535B10F2B6391702035D1F643F24F411072A0D901900100000A5290530F0000D0001170AA2A48410E32000000"));

        // The window draws from the slot array, not from the item records, so both halves are sent.
        // A slot whose item row is gone still reports itself occupied, giving a cell that can never
        // be filled.
        var missing = character.Items
            .Where(x => DbConnection.Items.FindById(x.Value) is null)
            .Select(x => x.Key)
            .ToList();

        if (missing.Count > 0)
        {
            foreach (var slot in missing)
            {
                character.Items.Remove(slot);
            }

            clientConnection.SaveSelectedCharacter();
            SphLogger.Warning($"SRV {localId:X4}: Cleared {missing.Count} slot(s) whose item is gone: " +
                              $"{string.Join(", ", missing.Select(x => Enum.GetName(x)))}");
        }

        SphLogger.Info($"SRV {localId:X4}: Declaring {character.Items.Count} carried item(s)");

        // Slot first, then the item that goes in it. An item in hand is also still in its inventory
        // slot, so it would otherwise be declared twice.
        var declared = new HashSet<int>();

        foreach (var (slot, itemId) in character.Items)
        {
            var item = DbConnection.Items.FindById(itemId);
            if (item is null || !declared.Add(itemId))
            {
                continue;
            }

            // The hand has no wire slot, but its item still has to be declared.
            var reserve = ItemSlotReserve.Build(localId, slot, item.Id, item.ItemCount);
            if (reserve is not null)
            {
                clientConnection.SendPacket(reserve);
            }

            clientConnection.SendPacket(ItemRecordEncoder.Encode((ushort) item.Id, (int) item.ObjectType,
                item.GameId, ItemRecordEncoder.NoSuffix, ByteSwap(localId)));
        }

        WaitForClientTimer = new(0.05f, false, clientConnection.MoveToNextBeforeGameStage);
    }
}