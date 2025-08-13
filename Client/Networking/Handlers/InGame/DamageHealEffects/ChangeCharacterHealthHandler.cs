using System;
using System.Threading.Tasks;
using static SphServer.Shared.BitStream.SphBitStream;
using static SphServer.Shared.Networking.DataModel.Serializers.SphereDbEntrySerializerBase;

namespace SphServer.Client.Networking.Handlers.InGame.DamageHealEffects;

public class ChangeCharacterHealthHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (double delta)
    {
        return;
    }

    public async Task HandleHealthChange (ushort entityId, int healthDiff)
    {
        var currentPlayerId = ByteSwap(localId);
        var character = clientConnection.GetSelectedCharacter()!;
        var playerId_1 = (byte) (((currentPlayerId & 0b1111) << 4) + 0b0111);
        var playerId_2 = (byte) ((currentPlayerId & 0b111111110000) >> 4);
        var mobId_1 = (byte) ((entityId & 0b1111111) << 1);
        var mobId_2 = (byte) ((entityId & 0b111111110000000) >> 7);
        var hpMod = healthDiff < 0 ? 0b1110 : 0b1100;
        healthDiff = Math.Abs(healthDiff);
        var dmg_1 = (byte) (((healthDiff & 0b1111) << 4) + hpMod + ((entityId & 0b1000000000000000) >> 15));
        var dmg_2 = (byte) ((healthDiff & 0b111111110000) >> 4);
        var dmg_3 = (byte) ((healthDiff & 0b11111111000000000000) >> 12);
        var playerId_3 = (byte) (0b10000000 + ((currentPlayerId & 0b1111000000000000) >> 12));
        var dmgPacket = new byte[]
        {
            0x1F, 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00, MinorByte(entityId), MajorByte(entityId), 0x48, 0x43, 0x65, 0x00,
            0x00, 0x00, 0x80, 0xA0, 0x03, 0x00, 0x00, 0xE0, playerId_1, playerId_2, playerId_3, 0x00, 0x24, mobId_1,
            mobId_2, dmg_1, dmg_2, dmg_3
        };
        var resultHp = character.CurrentHP - healthDiff;
        if (resultHp > character.MaxHP)
        {
            resultHp = character.MaxHP;
        }

        if (resultHp < 0)
        {
            resultHp = 0;
        }

        character.CurrentHP = (ushort) resultHp;
        clientConnection.MaybeQueueNetworkPacketSend(dmgPacket);
    }
}