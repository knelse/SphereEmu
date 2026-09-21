using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Packets;
using SphServer.Shared.Db;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.System;
using SphServer.Shared.Networking.DataModel.Serializers;
using static SphServer.Shared.BitStream.SphBitStream;

namespace SphServer.Client.Networking.Handlers.BeforeGame;

public class IngameAckHandler(ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    private SphereTimer? WaitForClientTimer;

    public async Task Handle(byte[] frame, double delta)
    {
        if (WaitForClientTimer is not null)
        {
            WaitForClientTimer.Tick(delta);
            return;
        }

        if (frame.Length != 0x13)
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

        var missing = character.Items
            .Where(x => DbConnection.Items.FindById(x.Value) is null)
            .Select(x => x.Key)
            .ToList();

        var shouldSave = false;

        if (missing.Count > 0)
        {
            foreach (var slot in missing)
            {
                character.Items.Remove(slot);
            }
            shouldSave = true;
            SphLogger.Warning($"SRV {localId:X4}: Cleared {missing.Count} slot(s) whose item is gone: " +
                              $"{string.Join(", ", missing.Select(x => Enum.GetName(x)))}");
        }

        character.SyncGuildFromWornEmblem();

        if (GuildAbilityLoadout.Sync(character, send: null))
        {
            shouldSave = true;
        }

        if (shouldSave)
        {
            clientConnection.SaveSelectedCharacter();
        }

        character.RecalcCurrentStats();

        SphLogger.Info($"SRV {localId:X4}: Declaring {character.Items.Count} carried item(s)");

        var declared = new HashSet<int>();

        foreach (var (slot, itemId) in character.Items)
        {
            var item = DbConnection.Items.FindById(itemId);
            if (item is null || !declared.Add(itemId))
            {
                continue;
            }

            var record = ItemRecordEncoder.Encode(item, ByteSwap(localId));

            if (slot == BelongingSlot.Helmet)
            {
                clientConnection.MaybeScheduleNetworkPacketSend(record);
                clientConnection.MaybeScheduleNetworkPacketSend(ItemSlotReserve.BuildSlotBinding(localId, slot, item.Id));
                continue;
            }

            var reserve = ItemSlotReserve.Build(localId, slot, item.Id, item.ItemCount);
            if (reserve is not null)
            {
                clientConnection.MaybeScheduleNetworkPacketSend(reserve);
            }

            clientConnection.MaybeScheduleNetworkPacketSend(record);
        }

        // After items: GuildPlus64/rank and worn bonuses need the emblem process to exist first.
        NetworkedStatsUpdater.Update(character, clientConnection.MaybeScheduleNetworkPacketSend, full: true);

        WaitForClientTimer = new(0.05f, false, clientConnection.MoveToNextBeforeGameStage);
    }
}
