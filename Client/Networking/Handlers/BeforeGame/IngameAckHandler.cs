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

        character.SyncGuildFromWornEmblem();
        if (GuildAbilityLoadout.Sync(character, send: null))
        {
            clientConnection.SaveSelectedCharacter();
        }

        // Nothing between logins keeps the derived stats in step with the item rows they came from,
        // so they are rebuilt from the slots before anything is sent.
        character.RecalcCurrentStats();

        SphLogger.Info($"SRV {localId:X4}: Sending game world data for [{character.Name}], " +
                       $"{(character.IsGenderFemale ? "female" : "male")}, face {character.FaceType}, " +
                       $"hair {character.HairStyle}/{character.HairColor}");

        // After the world-entry messages, not before them: sent earlier the client clears these
        // fields again before it builds the body.
        clientConnection.SendPacket(new CharacterDbEntrySerializer(character).ToGameDataByteArray());

        var worldData = CommonPackets.NewCharacterWorldData(character.ClientIndex);
        // clientConnection.SendPacket(worldData[0]);
        clientConnection.SendPacket(Convert.FromHexString(
            $"BA002C01000000{localId:X4}08C002D07911C8BD10445E0C222F08C91685C80B03581CC002011609B05080C5022C1860D1000B07593CC802021611B09080C5042C286051010B0B585CC00213799189BCD0445E6CC08203161DB0F080C5072C406011020B11588CC882441625B03081C5892D506091020B1558AC422C5870D1820B1758CCD082061635B0B0C1C603848F1535B10F2B6391702035D1F643F24F411072A0D901900100000A5290530F0000D0001170AA2A48410E32000000"));

        // Retail 08C0 login variants (same header form). Not wired.
        // same 128B template, May 2025 tail (D125 / 4A4B)
        // $"B3002C01000000{localId:X4}08C002D07911C8BD10445E0C222F08C91685C80B03581CC002011609B05080C5022C1860D1000B07593CC802021611B09080C5042C286051010B0B585CC00213799189BCD0445E6CC08203161DB0F080C5072C406011020B11588CC882441625B03081C5892D506091020B1558AC422C5870D1820B1758CCD082061635B0B0C1C6034438B556910FBB64D1C76F50F1F643F24F411072BCFF01900100000A5290C30D0000D0001170AA02"
        // $"B3002C01000000{localId:X4}08C002D07911C8BD10445E0C222F08C91685C80B03581CC002011609B05080C5022C1860D1000B07593CC802021611B09080C5042C286051010B0B585CC00213799189BCD0445E6CC08203161DB0F080C5072C406011020B11588CC882441625B03081C5892D506091020B1558AC422C5870D1820B1758CCD082061635B0B0C1C603C4C76A50B10FBB64D1C72F4AF1F643F24F411072409D00900100000A5290C30D0000D0001170AA02"
        // same template, truncated tail (EDDE)
        // $"9C002C01000000{localId:X4}08C002D07911C8BD10445E0C222F08C91685C80B03581CC002011609B05080C5022C1860D1000B07593CC802021611B09080C5042C286051010B0B585CC00213799189BCD0445E6CC08203161DB0F080C5072C406011020B11588CC882441625B03081C5892D506091020B1558AC422C5870D1820B1758CCD082061635B0B0C1C60304E15418910FBB64D1C74F4DF1F643F20F"
        // same template, longer Apr 2026 tail (ED3E)
        // $"C2002C01000000{localId:X4}08C002D07911C8BD10445E0C222F08C91685C80B03581CC002011609B05080C5022C1860D1000B07593CC802021611B09080C5042C286051010B0B585CC00213799189BCD0445E6CC08203161DB0F080C5072C406011020B11588CC882441625B03081C5892D506091020B1558AC422C5870D1820B1758CCD082061635B0B0C1C603041F2B18B10F2B639170A028D1F643F24F411072BCDB01900100000A5290530F00005090829C840000800688805355410A72B801000000"
        // different list, same footer (F08E)
        // $"A1002C01000000{localId:X4}08C00210791110115808222F06911784648B42E485812C0E6481208B045928C86241160CB2689085832C1E6401818B085C48E1620A1714B0A88085052C2E608189BCC8445E68222F3660C1018B0E5878C0E2031620B0088185082C466441228B125898C0E2C416288B04881850A2C5621162CB868C1850B2C666841038B1A58D84941107274EE01900100000A5290C30D0000D0001170AA02"
        // short live retail cousin (F4F0)
        // $"56002C01000000{localId:X4}08C022639DB4D0445E6CC0825367D1010B8F8FC0E22373B000F9192C426021028B115990C8A29463616AB738D1054AC46991020BD5C40F0C80C5AAC4820516AD120B1758CCD082461635B6B011"

        SphLogger.Info($"SRV {localId:X4}: Declaring {character.Items.Count} carried item(s)");

        // Slot first, then the item that goes in it — the order MutatorHandler uses. An item in hand
        // is also still in its inventory slot, so it would otherwise be declared twice.
        var declared = new HashSet<int>();

        foreach (var (slot, itemId) in character.Items)
        {
            var item = DbConnection.Items.FindById(itemId);
            if (item is null || !declared.Add(itemId))
            {
                continue;
            }

            var record = ItemRecordEncoder.Encode((ushort)item.Id, (int)item.WireObjectType,
                item.GameId, ItemRecordEncoder.SuffixWireFor(item), ByteSwap(localId));

            // Slot 0 is the exception: the reserve will not carry it, so the helm cell would stay
            // empty on every login even though dragging fills it. Bind that one the way a move does
            // — and after the record, because a move only ever names an item that already exists.
            if (slot == BelongingSlot.Helmet)
            {
                clientConnection.SendPacket(record);
                clientConnection.SendPacket(ItemSlotReserve.BuildSlotBinding(localId, slot, item.Id));
                continue;
            }

            // The hand has no wire slot, but its item still has to be declared.
            var reserve = ItemSlotReserve.Build(localId, slot, item.Id, item.ItemCount);
            if (reserve is not null)
            {
                clientConnection.SendPacket(reserve);
            }

            clientConnection.SendPacket(record);
        }

        // The spawn packet carries no attack numbers, so the stat window shows none until this. Last,
        // for the same reason the character record is: the client discards these if they arrive early.
        NetworkedStatsUpdater.Update(character);

        WaitForClientTimer = new(0.05f, false, clientConnection.MoveToNextBeforeGameStage);
    }
}
