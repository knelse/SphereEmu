using System;
using System.Collections.Generic;

namespace SphServer.Helpers.Networking;

public enum ClientPacketEvent
{
    None,
    InvalidOrTrailing,
    Unknown,
    PositionKeepalive,
    ProtocolControl,
    GroupAction,
    ItemPickup,
    ItemMove,
    ItemUse,
    ChatSend,
    ItemPickupToSlot,
    ContainerOpenLoot,
    ItemDrop,
    ItemDragOnGround,
    NpcInteract,
    ItemTakeMainhand,
    TradeBuy,
    CombatDamageTarget,
    ItemSwap,
    CharacterSelect,
    StatsUpdateRequest,
    NpcInteractClose
}

public readonly record struct ClientPacketClassification (
    ClientPacketEvent Event,
    string EventName,
    double Confidence,
    string Reason,
    bool IsEvent);

/// Length and signature bytes, shared by the emu dispatch and PacketLogViewer
public static class ClientPacketClassifier
{
    public static ClientPacketClassification ClassifyFrame (ReadOnlySpan<byte> frame)
    {
        if (frame.Length < ClientFrame.HeaderLength)
        {
            return Result (ClientPacketEvent.InvalidOrTrailing, 0, "shorter than a header", false);
        }

        // Channel sits in the plaintext header, so it is read before the body is de-obfuscated
        switch ((WireChannel) (frame[6] | (frame[7] << 8)))
        {
            case WireChannel.Keepalive:
            case WireChannel.SentCount:
            case WireChannel.Handshake:
            case WireChannel.HandshakeReply:
                return Result (ClientPacketEvent.ProtocolControl, 1.0, "transport channel", true);
            case WireChannel.Gameplay:
                break;
            default:
                return Result (ClientPacketEvent.Unknown, 0, "unrecognised channel", false);
        }

        // The record names the frame at every length; byte signatures match only when the position
        // block is absent
        if (frame.Length >= RecordHeaderMinimumLength)
        {
            var identity = GameplayRecord.ReadIdentity (frame);
            var byRecord = ClassifyByRecord (identity, frame);
            if (byRecord.HasValue)
            {
                return byRecord.Value;
            }
        }

        return ClassifyBySignature (frame);
    }

    /// Shortest gameplay frame with a whole record header
    private const int RecordHeaderMinimumLength = 30;

    /// Null when the record does not name the frame and the byte signature decides
    private static ClientPacketClassification? ClassifyByRecord (
        in GameplayRecord.Identity identity,
        ReadOnlySpan<byte> frame)
    {
        var frameLength = frame.Length;
        if (IsStatsUpdateRequest (frame))
        {
            return Result (ClientPacketEvent.StatsUpdateRequest, 1.0, "0x40 08 40 23 02 action 17", true);
        }

        if (identity.Tag == GameplayRecord.TagObjectInteract)
        {
            if (identity.SubjectType is GameplayRecord.SubjectTypeNpcTrade
                or GameplayRecord.SubjectTypeNpcQuestTitle)
            {
                // Closing an NPC window is a 0x20 frame with the player's own id, so the
                // damage fallback read it as a self-hit
                return Result (ClientPacketEvent.NpcInteractClose, 1.0, "record: NPC window close", true);
            }

            // Only the loot sack at the confirmed length is routed here
            return identity.SubjectType == GameplayRecord.SubjectTypeSackMobLoot &&
                   frameLength == ConfirmedLootContainerLength
                ? Result (ClientPacketEvent.ContainerOpenLoot, 1.0, "record: loot sack interaction", true)
                : null;
        }

        switch (GameplayRecord.ActionOf (identity))
        {
            case GameplayAction.PositionUpdate:
                return Result (ClientPacketEvent.PositionKeepalive, 1.0, "record: position", true);

            case GameplayAction.ChatSend:
                // The trigger opens a send; later frames carry the text
                return Result (ClientPacketEvent.ChatSend, 1.0,
                    frameLength == ChatTriggerLength ? "record: chat trigger" : "record: chat part", true);

            case GameplayAction.ChatPeriodic:
                // Same action code as chat with the flag set, and no text
                return Result (ClientPacketEvent.Unknown, 0, "record: periodic, no text", false);

            case GameplayAction.Attack:
                // Take-mainhand shares this action with a swing, so those shapes stay with the
                // signature table
                return LooksLikeTakeMainhand (frame)
                    ? null
                    : Result (ClientPacketEvent.CombatDamageTarget, 1.0, "record: attack", true);

            case GameplayAction.Buy:
                return Result (ClientPacketEvent.TradeBuy, 1.0, "record: buy request", true);

            // Action 3 is several lengths the signature table already splits

            case GameplayAction.Telemetry:
                // Character select is the 35-byte 08 40 80 05 form; other telemetry lengths have no
                // handler
                return IsCharacterSelect (frame)
                    ? Result (ClientPacketEvent.CharacterSelect, 1.0, CharacterSelectReason (frame), true)
                    : Result (ClientPacketEvent.Unknown, 0, "record: client telemetry", false);

            case GameplayAction.Unknown when identity.Tag == GameplayRecord.TagPlayerAction
                                             && identity.ActionCode == 0:
                // Same bytes as trade-buy, but a purchase is action 8
                return Result (ClientPacketEvent.Unknown, 0, "record: action 0, not understood", false);

            default:
                // GroupActionOrPickup has no record-level discriminator
                return null;
        }
    }

    public static byte ShiftedByte (ReadOnlySpan<byte> frame, int legacyByteIndex)
    {
        var bit = legacyByteIndex * 8 + 110;
        return frame.Length * 8 >= bit + 8 ? (byte) GameplayRecord.ReadBits (frame, bit, 8) : (byte) 0;
    }

    /// Length 0x40, 08 40 23 02, action 17, eight i32 deltas from bit 251: str agi acc end earth
    /// air water fire
    public static bool IsStatsUpdateRequest (ReadOnlySpan<byte> frame)
    {
        if (frame.Length != StatsUpdateRequestLength
            || (frame[0] | (frame[1] << 8)) != StatsUpdateRequestLength
            || ShiftedByte (frame, 13) != 0x08
            || ShiftedByte (frame, 14) != 0x40
            || ShiftedByte (frame, 15) != 0x23
            || ShiftedByte (frame, 16) != 0x02)
        {
            return false;
        }

        var identity = GameplayRecord.ReadIdentity (frame);
        return !identity.PositionFlag
               && identity.Tag == GameplayRecord.TagPlayerAction
               && identity.ActionCode == 17
               && !identity.ActionFlag;
    }

    /// Length 0x23, 08 40 80 05, slot byte is 04/08/0C, tail 04 08 00
    public static bool IsCharacterSelect (ReadOnlySpan<byte> frame) =>
        frame.Length == CharacterSelectLength
        && (frame[0] | (frame[1] << 8)) == CharacterSelectLength
        && ShiftedByte (frame, 13) == 0x08
        && ShiftedByte (frame, 14) == 0x40
        && ShiftedByte (frame, 15) == 0x80
        && ShiftedByte (frame, 16) == 0x05
        && ShiftedByte (frame, 17) is 0x04 or 0x08 or 0x0C
        && ShiftedByte (frame, 18) == 0x04
        && ShiftedByte (frame, 19) == 0x08
        && ShiftedByte (frame, 20) == 0x00;

    private static string CharacterSelectReason (ReadOnlySpan<byte> frame) =>
        $"0x23 08 40 80 05 slot {ShiftedByte (frame, 17) / 4 - 1}";

    public static bool IsHandChangeSignature (ReadOnlySpan<byte> frame) =>
        MainhandFrame.TryRead (frame, out _);

    /// Take or put-down frames that share the attack action with a real swing
    private static bool LooksLikeTakeMainhand (ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 30)
        {
            return false;
        }

        if (IsHandChangeSignature (frame))
        {
            return true;
        }

        var b13 = ShiftedByte (frame, 13);
        var b14 = ShiftedByte (frame, 14);
        var b15 = ShiftedByte (frame, 15);
        if (b13 != 0x08 || b14 != 0x40 || b15 is not (0xA3 or 0x83))
        {
            return false;
        }

        var declaredLength = frame[0] | (frame[1] << 8);
        return MainhandFrame.IsExclusiveTakeLength (declaredLength)
               || declaredLength == 0x41 && b15 == 0x83;
    }

    private const int ChatTriggerLength = 40;
    private const int CharacterSelectLength = 0x23;
    private const int StatsUpdateRequestLength = 0x40;

    /// Open-loot length with two 16-byte guids
    private const int ConfirmedLootContainerLength = 0x37;

    private static ClientPacketClassification ClassifyBySignature (ReadOnlySpan<byte> frame)
    {
        // Chat text parts exceed 255 bytes, so the length is 16-bit
        var declaredLength = frame[0] | (frame[1] << 8);
        if (declaredLength != frame.Length)
        {
            // Some NPC frames keep a non-canonical first-byte length
            if (frame.Length >= 30 &&
                ShiftedByte (frame, 13) == 0x08 && ShiftedByte (frame, 14) == 0x40 &&
                ShiftedByte (frame, 15) == 0xA3 &&
                declaredLength is 0x31 or 0x36 or 0x5B or 0x60)
            {
                return Result (
                    ClientPacketEvent.NpcInteract,
                    1.0,
                    "NPC signature 08 40 A3; noncanonical length",
                    true);
            }

            return Result (ClientPacketEvent.InvalidOrTrailing, 0, "frame length is invalid", false);
        }

        // 0x34 is the position keepalive; 08 40 A3 has to miss before this is a keepalive
        if (declaredLength == 0x34 &&
            !(frame.Length >= 30 && ShiftedByte (frame, 13) == 0x08 && ShiftedByte (frame, 14) == 0x40 &&
              ShiftedByte (frame, 15) == 0xA3))
        {
            return Result (ClientPacketEvent.PositionKeepalive, 1.0, "ClientConnection case 0x34", true);
        }

        if (declaredLength is 0x08 or 0x0C || frame.Length <= 12)
        {
            return Result (ClientPacketEvent.ProtocolControl, 0.75, "short control frame", true);
        }

        if (frame.Length < 30)
        {
            return Result (ClientPacketEvent.Unknown, 0, "no known handler signature", false);
        }

        var b13 = ShiftedByte (frame, 13);
        var b14 = ShiftedByte (frame, 14);
        var b15 = ShiftedByte (frame, 15);

        switch (declaredLength)
        {
            case 0x3D when frame.Length >= 59 && ShiftedByte (frame, 41) == 0x08 &&
                           ShiftedByte (frame, 42) == 0x40 &&
                           ShiftedByte (frame, 43) == 0x23 && ShiftedByte (frame, 44) == 0x23:
                return Result (ClientPacketEvent.GroupAction, 1.0, "handler signature 08 40 23", true);

            case 0x24 when b13 == 0x08 && b14 == 0x40 && b15 == 0x23:
                return Result (ClientPacketEvent.ItemPickup, 1.0, "handler signature 08 40 23", true);

            case 0x40 when IsStatsUpdateRequest (frame):
                return Result (ClientPacketEvent.StatsUpdateRequest, 1.0, "0x40 08 40 23 02 action 17", true);

            case 0x33 when b13 == 0x08 && b14 == 0x40 && b15 == 0x81:
                return Result (ClientPacketEvent.ItemMove, 1.0, "handler signature 08 40 81", true);

            case 0x33 when b13 == 0x08 && b14 == 0x40 && b15 == 0x63:
                return Result (ClientPacketEvent.ItemDragOnGround, 1.0, "handler signature 08 40 63", true);

            case 0x32 or 0x33:
                return Result (ClientPacketEvent.ItemUse, 1.0, "ClientConnection item use fallback", true);

            case 0x28 when b13 == 0x08 && b14 == 0x40 && b15 == 0x43:
                return Result (ClientPacketEvent.ChatSend, 1.0, "handler signature 08 40 43", true);

            case 0x44 when b13 == 0x08 && b14 == 0x40 && b15 == 0xC1:
                return Result (ClientPacketEvent.ItemPickupToSlot, 1.0, "handler signature 08 40 C1", true);

            case 0x36 when b13 == 0x5C && b14 == 0x46 && b15 == 0xE1:
                return Result (ClientPacketEvent.ContainerOpenLoot, 1.0, "handler signature 5C 46 E1", true);

            case 0x2F when b15 == 0xE1 && (b14 & 0xF0) == 0x40:
                return Result (ClientPacketEvent.ItemUse, 1.0, "use opcode E14", true);

            case 0x23 when IsCharacterSelect (frame):
                return Result (ClientPacketEvent.CharacterSelect, 1.0, CharacterSelectReason (frame), true);

            case 0x3B when b13 == 0x08 && b14 == 0x40 && b15 == 0x63:
                return Result (ClientPacketEvent.ItemDrop, 1.0, "handler signature 08 40 63", true);

            case 0x5B or 0x60 when b13 == 0x08 && b14 == 0x40 && b15 == 0xA3:
                return Result (ClientPacketEvent.NpcInteract, 1.0, "NPC signature 08 40 A3", true);

            case 0x4C when b13 == 0x08 && b14 == 0x40 && b15 == 0xA3:
                return Result (ClientPacketEvent.ItemSwap, 1.0, "swap signature 08 40 A3", true);

            case 0x2F or 0x43 or 0x47 or 0x4B
                when b13 == 0x08 && b14 == 0x40 && b15 is 0xA3 or 0x83:
                return Result (
                    ClientPacketEvent.ItemTakeMainhand,
                    1.0,
                    b15 == 0x83 ? "handler signature 08 40 83" : "handler signature 08 40 A3",
                    true);

            case 0x41 when b13 == 0x08 && b14 == 0x40 && b15 == 0x83:
                // Switching to bare fists.
                return Result (ClientPacketEvent.ItemTakeMainhand, 1.0, "handler signature 08 40 83", true);

            case 0x35 or 0x3C when b13 == 0x08 && b14 == 0x40 && b15 == 0x03:
                return Result (ClientPacketEvent.TradeBuy, 1.0, "handler signature 08 40 03", true);

            case 0x41 or 0x48 or 0x62 when b13 == 0x08 && b14 == 0x40 && b15 == 0xA3 &&
                                           IsHandChangeSignature (frame):
                return Result (ClientPacketEvent.ItemTakeMainhand, 1.0, "hand-change signature 41", true);

            case 0x62 when b13 == 0x08 && b14 == 0x40 && b15 == 0xA3:
                return Result (ClientPacketEvent.CombatDamageTarget, 1.0, "armed attack 08 40 A3", true);

            case 0x41 or 0x48:
                return Result (
                    ClientPacketEvent.CombatDamageTarget,
                    1.0,
                    "ClientConnection damage fallback",
                    true);
        }

        // Signature fallback when the length is unexpected and the payload still matches
        if (b13 == 0x08 && b14 == 0x40)
        {
            return b15 switch
            {
                // Trigger and text parts share 08 40 43
                0x43 => Result (ClientPacketEvent.ChatSend, 1.0, "handler signature 08 40 43", true),
                0x83 => Result (ClientPacketEvent.ItemTakeMainhand, 1.0, "handler signature 08 40 83", true),
                0xA3 => Result (ClientPacketEvent.ItemTakeMainhand, 1.0, "handler signature 08 40 A3", true),
                0x63 => Result (ClientPacketEvent.ItemDrop, 0.9, "handler signature 08 40 63", true),
                0x23 => IsStatsUpdateRequest (frame)
                    ? Result (ClientPacketEvent.StatsUpdateRequest, 1.0, "0x40 08 40 23 02 action 17", true)
                    : Result (ClientPacketEvent.GroupAction, 0.9, "handler signature 08 40 23", true),
                0x81 => Result (ClientPacketEvent.ItemMove, 0.9, "handler signature 08 40 81", true),
                0xC1 => Result (ClientPacketEvent.ItemPickupToSlot, 0.9, "handler signature 08 40 C1", true),
                0x03 => Result (ClientPacketEvent.TradeBuy, 0.9, "handler signature 08 40 03", true),
                _ => Result (ClientPacketEvent.Unknown, 0, "no known handler signature", false)
            };
        }

        if (b13 == 0x5C && b14 == 0x46 && b15 == 0xE1)
        {
            return Result (ClientPacketEvent.ContainerOpenLoot, 0.9, "handler signature 5C 46 E1", true);
        }

        return Result (ClientPacketEvent.Unknown, 0, "no known handler signature", false);
    }

    public static string ToEventName (ClientPacketEvent packetEvent) =>
        packetEvent switch
        {
            ClientPacketEvent.InvalidOrTrailing => "client.invalid_or_trailing",
            ClientPacketEvent.Unknown => "client.unknown",
            ClientPacketEvent.PositionKeepalive => "client.position_keepalive",
            ClientPacketEvent.ProtocolControl => "client.protocol.control",
            ClientPacketEvent.GroupAction => "client.group.action",
            ClientPacketEvent.ItemPickup => "client.item.pickup",
            ClientPacketEvent.ItemMove => "client.item.move",
            ClientPacketEvent.ItemUse => "client.item.use",
            ClientPacketEvent.ChatSend => "client.chat.send",
            ClientPacketEvent.ItemPickupToSlot => "client.item.pickup_to_slot",
            ClientPacketEvent.ContainerOpenLoot => "client.container.open_loot",
            ClientPacketEvent.ItemDrop => "client.item.drop",
            ClientPacketEvent.ItemDragOnGround => "client.item.drag_on_ground",
            ClientPacketEvent.NpcInteract => "client.npc.interact",
            ClientPacketEvent.ItemTakeMainhand => "client.item.take_mainhand",
            ClientPacketEvent.TradeBuy => "client.trade.buy",
            ClientPacketEvent.CombatDamageTarget => "client.combat.damage_and_send_animation",
            ClientPacketEvent.ItemSwap => "client.item.swap",
            ClientPacketEvent.CharacterSelect => "client.character_select",
            ClientPacketEvent.StatsUpdateRequest => "client.stats.update.request",
            ClientPacketEvent.NpcInteractClose => "client.npc.interact_close",
            _ => "client.none"
        };

    private static ClientPacketClassification Result (
        ClientPacketEvent packetEvent,
        double confidence,
        string reason,
        bool isEvent) =>
        new (packetEvent, ToEventName (packetEvent), confidence, reason, isEvent);
}
