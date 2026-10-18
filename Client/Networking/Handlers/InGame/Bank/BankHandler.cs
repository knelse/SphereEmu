using System;
using System.Buffers.Binary;
using System.Threading.Tasks;
using BitStreams;
using SphereHelpers.Extensions;
using SphServer.Helpers.Networking;
using SphServer.Packets;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;
using static SphServer.Shared.BitStream.SphBitStream;
using static SphServer.Shared.Networking.DataModel.Serializers.SphereDbEntrySerializerBase;

namespace SphServer.Client.Networking.Handlers.InGame.Bank;

/// <summary>
/// The player's bank account. A banker's UseOwner asks _player.GetBank for the process in client
/// slot 58 and does nothing without one; that process (_bank, ObjectType.Bank) draws the window and
/// talks to the server with region 12 cmd 15 in both directions
/// </summary>
public class BankHandler (ushort localId, SphereClient client)
    : ISphereClientNetworkingHandler
{
    private const int BankSlotWireId = 58;

    /// <summary>
    /// _bank region 12 uses a 5-bit command followed by an array8 payload
    /// </summary>
    private const byte BankRegionWire = 13;

    private const byte UserCommand = 15;
    private const int UserCommandWidth = 5;

    /// <summary>
    /// Command right after the 7-bit tag, as in a backpack's ContMan
    /// </summary>
    private const int CommandBit = ClientFrame.BodyOffset * 8 + 53;

    /// <summary>
    /// _bank.StartUp sends it once the bank process exists
    /// </summary>
    private const byte QueryInitRequest = 120;

    /// <summary>
    /// BankOper: operation byte, then a u32 amount; WinSelcount uses 0 to put money in, 1 to take it out
    /// </summary>
    private const byte DepositOperation = 0;

    private const byte WithdrawOperation = 1;

    /// <summary>
    /// First byte of a reply, as _bank.RcvUser reads it: balance (u64), password status, commission
    /// </summary>
    private const byte BalanceReply = 0;

    private const byte StatusReply = 1;
    private const byte CommissionReply = 2;

    /// <summary>
    /// Until a status arrives the window refuses to open with "the bank is not initialized"
    /// </summary>
    private const byte NoPasswordStatus = 4;

    /// <summary>
    /// WinSelcount does not offer a withdrawal that would leave more than this on hand
    /// </summary>
    private const long MaxMoneyOnHand = 2_000_000_000;

    private ushort bankId;

    /// <summary>
    /// The account lives on the character, so the bank process is session-only and needs no stored item
    /// </summary>
    public void Declare ()
    {
        if (bankId == 0)
        {
            bankId = WorldObjectIndex.NewItem ();
        }

        var bank = new ItemDbEntry
        {
            Id = bankId,
            ObjectType = ObjectType.Bank,
            ItemCount = 1
        };

        client.MaybeQueueNetworkPacketSend (ItemSlotReserve.BuildRaw (localId, BankSlotWireId, bankId));
        client.MaybeQueueNetworkPacketSend (ItemRecordEncoder.Encode (bank, ByteSwap (localId)));
        SphLogger.Info ($"Bank {bankId:X4} declared in slot {BankSlotWireId}. Client ID: {localId:X4}");
    }

    public Task Handle (byte[] frame, double delta)
    {
        var identity = GameplayRecord.ReadIdentity (frame);
        if (bankId == 0 || identity.EntityId != bankId || identity.Tag != BankRegionWire)
        {
            return Task.CompletedTask;
        }
        var shift = identity.PositionFlag ? GameplayRecord.PositionBlockWidth : 0;
        var payloadBit = CommandBit + shift + UserCommandWidth + 8;
        if (payloadBit > frame.Length * 8)
        {
            return Task.CompletedTask;
        }
        var stream = new BitStream (frame);
        stream.ReadBits (CommandBit + shift);
        var command = stream.ReadByte (UserCommandWidth);
        var length = (int) stream.ReadByte (8);
        if (payloadBit + length * 8 > frame.Length * 8)
        {
            return Task.CompletedTask;
        }
        var payload = new byte[length];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = stream.ReadByte (8);
        }

        SphLogger.Info ($"Bank {identity.EntityId:X4}: cmd {command} data {Convert.ToHexString (payload)} " +
                        $"frame {Convert.ToHexString (frame)}. Client ID: {localId:X4}");

        var character = client.CurrentCharacter;
        if (character is null || bankId == 0 || command != UserCommand || length == 0)
        {
            return Task.CompletedTask;
        }

        switch (payload[0])
        {
            case QueryInitRequest:
                Send ([StatusReply, NoPasswordStatus]);
                Send ([CommissionReply, 0]);
                SendBalance (character);
                break;
            case DepositOperation when length == 5:
                Deposit (character, BinaryPrimitives.ReadUInt32LittleEndian (payload.AsSpan (1)));
                break;
            case WithdrawOperation when length == 5:
                Withdraw (character, BinaryPrimitives.ReadUInt32LittleEndian (payload.AsSpan (1)));
                break;
            default:
                // Password, reset and buying through the bank are not implemented
                SphLogger.Warning ($"Bank: request {payload[0]} is not supported. Client ID: {localId:X4}");
                break;
        }

        return Task.CompletedTask;
    }

    private void Deposit (CharacterDbEntry character, uint amount)
    {
        if (amount == 0 || amount > character.Money || character.BankMoney > long.MaxValue - amount)
        {
            SphLogger.Warning ($"Bank: cannot put {amount}, has {character.Money}. Client ID: {localId:X4}");
            SendBalance (character);
            return;
        }

        character.Money -= (int) amount;
        character.BankMoney += amount;
        Commit (character, $"put {amount}");
    }

    private void Withdraw (CharacterDbEntry character, uint amount)
    {
        if (amount == 0 || amount > character.BankMoney || character.Money + (long) amount > MaxMoneyOnHand)
        {
            SphLogger.Warning ($"Bank: cannot take {amount}, account {character.BankMoney}, " +
                               $"has {character.Money}. Client ID: {localId:X4}");
            SendBalance (character);
            return;
        }

        character.BankMoney -= amount;
        character.Money += (int) amount;
        Commit (character, $"took {amount}");
    }

    private void Commit (CharacterDbEntry character, string what)
    {
        client.SendMoney ();
        SendBalance (character);
        client.SaveCharacter ();
        SphLogger.Info ($"Bank: {what}, account {character.BankMoney}, money {character.Money}. " +
                        $"Client ID: {localId:X4}");
    }

    private void SendBalance (CharacterDbEntry character)
    {
        var reply = new byte[9];
        reply[0] = BalanceReply;
        BinaryPrimitives.WriteInt64LittleEndian (reply.AsSpan (1), character.BankMoney);
        Send (reply);
    }

    private void Send (byte[] payload)
    {
        var stream = GetWriteBitStream ();
        stream.WriteUInt16 (bankId, 16);
        stream.WriteByte (0, 2);
        stream.WriteUInt16 ((ushort) ObjectType.Bank, 12);
        stream.WriteByte (BankRegionWire, 7);
        stream.WriteByte (UserCommand, UserCommandWidth);
        stream.WriteByte ((byte) payload.Length, 8);
        foreach (var b in payload)
        {
            stream.WriteByte (b, 8);
        }

        stream.WriteByte (0, 7);
        client.MaybeQueueNetworkPacketSend (Packet.ToByteArray (stream.GetStreamData (), 3));
    }

    public void Release ()
    {
        WorldObjectIndex.Release (bankId);
        bankId = 0;
    }
}
