using System;
using System.Threading.Tasks;
using BitStreams;
using SphServer.Packets;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.WorldState;
using SphServer.System;
using static SphServer.Shared.BitStream.SphBitStream;
using static SphServer.Shared.Networking.DataModel.Serializers.SphereDbEntrySerializerBase;

namespace SphServer.Client.Networking.Handlers.InGame.DamageHealEffects;

public class DamageTargetHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    private int newPlayerDungeonMobHp = 64;

    public async Task Handle (double delta)
    {
        // TODO: actual damage with actual mob node
        var receiveStream = new BitStream(clientConnection.ReceiveBuffer);
        var character = clientConnection.GetSelectedCharacter()!;
        receiveStream.ReadBits(172);
        var targetId = receiveStream.ReadUInt16();
        var packet = PacketPart.LoadDefinedWithOverride("fist_attack_target");
        PacketPart.UpdateEntityId(packet, targetId);
        PacketPart.UpdateValue(packet, "attacker_id", character.ClientIndex, 16);
        PacketPart.UpdateValue(packet, "30000_minus_damage", 30000);
        var packetBytes = PacketPart.GetBytesToWrite(packet);
        clientConnection.MaybeQueueNetworkPacketSend(packetBytes);
        return;

        var paAbs = Math.Abs(character.PAtk);
        var currentItem = DbConnection.Items.FindById(character.Items[BelongingSlot.MainHand]);
        var damagePa = currentItem.PAtkNegative == 0
            ? 0
            : SphRng.Rng.Next((int) (paAbs * 0.65), (int)
                (paAbs * 1.4));
        var damageMa = character.MAtk;
        var totalDamage = (ushort) (damageMa + damagePa);
        var destId = (ushort) GetDestinationIdFromDamagePacket(clientConnection.ReceiveBuffer);
        var playerIndexByteSwap = ByteSwap(localId);
        var selfDamage = destId == playerIndexByteSwap;
        var selfHeal = damageMa > 0;

        if (selfDamage)
        {
            var id_1 = (byte) (((ByteSwap(localId) & 0b111) << 5) + 0b00010);
            var id_2 = (byte) ((ByteSwap(localId) >> 3) & 0b11111111);
            var type = selfHeal ? 0b10000000 : 0b10100000;
            var id_3 = (byte) (((ByteSwap(localId) >> 11) & 0b11111) + type);

            if (selfHeal)
            {
                if (character.CurrentHP + totalDamage > character.MaxHP)
                {
                    totalDamage = (ushort) (character.MaxHP - character.CurrentHP);
                }

                character.CurrentHP += totalDamage;
            }
            else
            {
                if (character.CurrentHP < totalDamage)
                {
                    totalDamage = character.CurrentHP;
                }

                character.CurrentHP -= totalDamage;
            }

            var selfDamagePacket = new byte[]
            {
                0x11, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MajorByte(localId), MinorByte(localId), 0x08, 0x40, id_1,
                id_2, id_3, MinorByte(totalDamage), MajorByte(totalDamage), 0x00
            };
            clientConnection.MaybeQueueNetworkPacketSend(selfDamagePacket);
        }
        else
        {
            newPlayerDungeonMobHp = Math.Max(0, newPlayerDungeonMobHp - totalDamage);

            if (newPlayerDungeonMobHp > 0)
            {
                var src_1 = (byte) ((playerIndexByteSwap & 0b1111111) << 1);
                var src_2 = (byte) ((playerIndexByteSwap & 0b111111110000000) >> 7);
                var src_3 = (byte) ((playerIndexByteSwap & 0b1000000000000000) >> 15);
                var dmg_1 = (byte) (0x60 - totalDamage * 2);
                var hp_1 = (byte) ((newPlayerDungeonMobHp & 0b1111) << 4);
                var hp_2 = (byte) ((newPlayerDungeonMobHp & 0b11110000) >> 4);
                var damagePacket = new byte[]
                {
                    0x1B, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x04, MinorByte(destId), MajorByte(destId), 0x48,
                    0x43, 0xA1, 0x0B, src_1, src_2, src_3, dmg_1, 0xEA, 0x0A, 0x6D, hp_1, hp_2, 0x00,
                    0x04, 0x50, 0x07, 0x00
                };
                clientConnection.MaybeQueueNetworkPacketSend(damagePacket);
            }
            else
            {
                var moneyReward = (byte) (10 + SphRng.Rng.Next(0, 9));
                character.Money += moneyReward;
                var totalMoney_1 = (byte) (((character.Money & 0b11111) << 3) + 0b100);
                var totalMoney_2 = (byte) ((character.Money & 0b11100000) >> 5);
                character.KarmaCount += 1;
                var karma_1 = (byte) (((character.KarmaCount & 0b1111111) << 1) + 1);
                var src_1 = (byte) ((playerIndexByteSwap & 0b1000000000000000) >> 15);
                var src_2 = (byte) ((playerIndexByteSwap & 0b111111110000000) >> 7);
                var src_3 = (byte) ((playerIndexByteSwap & 0b1111111) << 1);
                var src_4 = (byte) (((playerIndexByteSwap & 0b111) << 5) + 0b01111);
                var src_5 = (byte) ((playerIndexByteSwap & 0b11111111000) >> 3);
                var src_6 = (byte) ((playerIndexByteSwap & 0b1111100000000000) >> 11);

                var moneyReward_1 = (byte) (((moneyReward & 0b11) << 6) + 1);
                var moneyReward_2 = (byte) ((moneyReward & 0b1111111100) >> 2);

                // this packet can technically contain any stat, xp, level, hp/mp, etc
                // for the new player dungeon we only care about giving karma and some money after a kill
                // chat message should be bright green, idk how to get it to work though
                var deathPacket = new byte[]
                {
                    0x04, MinorByte(destId), MajorByte(destId), 0x48, 0x43, 0xA1, 0x09, src_3, src_2, src_1,
                    0x00, 0x7E, MinorByte(playerIndexByteSwap), MajorByte(playerIndexByteSwap), 0x08, 0x40,
                    0x41, 0x0A, 0x34, 0x3A, 0x93, 0x00, 0x00, 0x7E, 0x14, 0xCE, 0x14, 0x47, 0x81, 0x05, 0x3A,
                    0x93, 0x7E, MinorByte(destId), MajorByte(destId), 0x00, 0xC0, src_4, src_5, src_6, 0x01,
                    0x58, 0xE4, totalMoney_1, totalMoney_2, 0x16, 0x28, karma_1, 0x80, 0x46, 0x40,
                    moneyReward_1, moneyReward_2
                };
                clientConnection.MaybeQueueNetworkPacketSend(Packet.ToByteArray(deathPacket));
                var mob = DbConnection.Monsters.FindById((int) destId);
                if (mob.ParentNodeId is not null)
                {
                    var parentNode = ActiveNodes.Get(mob.ParentNodeId.Value) as Godot.Nodes.MobNode;
                    if (parentNode is not null)
                    {
                        ItemContainerDbEntry.CreateHierarchyWithContents(parentNode.GlobalTransform.Origin.X,
                            parentNode.GlobalTransform.Origin.Y,
                            parentNode.GlobalTransform.Origin.Z, 0, LootRatity.DEFAULT_MOB);
                        parentNode.SetInactive();
                    }
                }
            }
        }
    }

    private static int GetDestinationIdFromDamagePacket (byte[] ReceiveBuffer)
    {
        var destBytes = ReceiveBuffer[28..];

        return ((destBytes[2] & 0b11111) << 11) + (destBytes[1] << 3) + ((destBytes[0] & 0b11100000) >> 5);
    }
}