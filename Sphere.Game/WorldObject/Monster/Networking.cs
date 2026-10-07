using System;
using Godot;
using SphereHelpers.Extensions;
using SphServer.Client;
using SphServer.Packets;
using SphServer.Shared.BitStream;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.Networking.Mbc;

namespace SphServer.Sphere.Game.WorldObject;

public partial class Monster
{
    private int GetMonsterLevel () => MonsterInstance?.Level ?? level;

    protected override ushort GetMoveModuleTag () => MbcModuleTag ();

    private ushort MbcModuleTag ()
    {
        var objectType = MonsterInstance?.MonsterDataOrigin.ObjectType ?? GameObjectType.Monster;
        return (ushort) (objectType is GameObjectType.Monster_Flying or GameObjectType.Monster_Event_Flying
            or GameObjectType.Special_Necromancer_Flyer
            ? ObjectType.Monster_Flyer
            : ObjectType.Monster);
    }

    /// <summary>
    /// A client entering range after the killing blow would get a live spawn and miss the death
    /// </summary>
    protected override void ShowForClient (SphereClient client)
    {
        if (_deathStarted)
        {
            return;
        }

        if (!MonsterTypeMapping.MonsterNameToMonsterTypeMapping.TryGetValue (MonsterType, out var gameId))
        {
            SphLogger.Error ($"Monster spawn: no type id for {MonsterType}. Id: {ID:X4}");
            return;
        }

        var origin = GlobalTransform.Origin;
        var entityId = client.GetLocalObjectId (ID);
        var packedLevel = Math.Max (0, GetMonsterLevel () - 1);
        var currentHp = MonsterInstance?.CurrentHp ?? 50;
        var maxHp = MonsterInstance?.MaxHp ?? 50;
        var nameCode = ResolveNameCode ();
        client.MaybeQueueNetworkPacketSend (BuildSpawnSnapshot (
            entityId, MbcModuleTag (),
            origin.X, -origin.Y, -origin.Z, DecodeAngleToYawRadians (Angle),
            currentHp, maxHp, gameId, packedLevel, nameCode));
    }

    /// <summary>
    /// Named mobs send a syllable code; -1 lets the client use the species string
    /// </summary>
    private int ResolveNameCode ()
    {
        if (NamedBossRank == NamedBossRank.None)
        {
            return -1;
        }

        if (NameCode == -1)
        {
            NameCode = MonsterNameCode.Roll (out _);
        }

        return NameCode;
    }

    /// <summary>
    /// Region 61 spawn; packed level is 0-based (display is max(i37, i38)+1), and nameCode other
    /// than -1 appends TradeMan cmd 3
    /// </summary>
    private static byte[] BuildSpawnSnapshot (ushort entityId, ushort moduleTag,
        float x, float y, float z, double angleRadians,
        int currentHp, int maxHp, int gameId, int packedLevel, int nameCode)
    {
        var stream = SphBitStream.GetWriteBitStream ();
        stream.WriteByte (0, 1); // has_position
        stream.WriteUInt16 (0, 15); // tick
        stream.WriteUInt16 (entityId, 16);
        stream.WriteByte (0, 2); // process_id high
        stream.WriteUInt16 ((ushort) (moduleTag & 0xFFF), 12);
        stream.WriteByte (62, 7); // wire = region 61 + 1
        WriteIeeeFloat (stream, x);
        WriteIeeeFloat (stream, y);
        WriteIeeeFloat (stream, z);
        stream.WriteByte (MbcCoordEncoding.EncodeAngle (angleRadians), 8);
        stream.WriteByte (0, 2); // contain_state
        CommonPackets.WriteMbcVarint (stream, currentHp);
        CommonPackets.WriteMbcVarint (stream, maxHp);
        CommonPackets.WriteMbcVarint (stream, gameId);
        CommonPackets.WriteMbcVarint (stream, packedLevel);
        if (nameCode != -1)
        {
            stream.WriteByte (10, 7); // wire = region 9 + 1 (TradeMan)
            stream.WriteByte (3, 4); // command
            stream.WriteByte (4, 4); // payload length
                                     // IntToBits drops a negative int32, so the code goes out as
                                     // two u16s, low half first.
            var bits = unchecked ((uint) nameCode);
            stream.WriteUInt16 ((ushort) bits, 16);
            stream.WriteUInt16 ((ushort) (bits >> 16), 16);
        }

        return Packet.ToByteArray (stream.GetStreamData (), 1);
    }

    private static void WriteIeeeFloat (SphWriteStream stream, float value)
    {
        var bits = BitConverter.SingleToUInt32Bits (value);
        stream.WriteUInt16 ((ushort) bits, 16);
        stream.WriteUInt16 ((ushort) (bits >> 16), 16);
    }
}
