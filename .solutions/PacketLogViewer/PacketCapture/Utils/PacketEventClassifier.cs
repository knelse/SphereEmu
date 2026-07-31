using System;
using System.Text;
using SphServer.Helpers;

namespace PacketLogViewer;

public readonly record struct PacketEventClassification(
    string EventName,
    double Confidence,
    string Reason,
    bool IsEvent);

internal static class PacketEventClassifier
{
    public static PacketEventClassification ClassifyClientFrame(ReadOnlySpan<byte> frame)
    {
        if (frame.Length == 0)
        {
            return new PacketEventClassification("client.invalid_or_trailing", 0, "empty frame", false);
        }

        var declaredLength = frame[0];
        if (declaredLength != frame.Length)
        {
            // Some NPC frames are accepted with a non-canonical first-byte length.
            if (frame.Length >= 16 &&
                frame[13] == 0x08 && frame[14] == 0x40 && frame[15] == 0xA3 &&
                declaredLength is 0x31 or 0x36)
            {
                return new PacketEventClassification(
                    "client.npc.interact", 1.0, "NPC signature 08 40 A3; noncanonical length", true);
            }

            return new PacketEventClassification(
                "client.invalid_or_trailing", 0, "frame length is invalid", false);
        }

        if (declaredLength == 0x26)
        {
            return new PacketEventClassification(
                "client.position_keepalive", 1.0, "ClientConnection case 0x26", true);
        }

        if (declaredLength is 0x08 or 0x0C || frame.Length <= 12)
        {
            return new PacketEventClassification(
                "client.protocol.control", 0.75, "short control frame", true);
        }

        if (frame.Length < 16)
        {
            return new PacketEventClassification(
                "client.unknown", 0, "no known handler signature", false);
        }

        var b13 = frame[13];
        var b14 = frame[14];
        var b15 = frame[15];

        switch (declaredLength)
        {
            case 0x13 when b13 == 0x08 && b14 == 0x40 && b15 == 0x23 &&
                           frame.Length > 16 && frame[16] == 0x23:
                return new PacketEventClassification(
                    "client.group.action", 1.0, "handler signature 08 40 23", true);

            case 0x16 when b13 == 0x08 && b14 == 0x40 && b15 == 0x23:
                return new PacketEventClassification(
                    "client.item.pickup", 1.0, "handler signature 08 40 23", true);

            case 0x18 when b13 == 0x08 && b14 == 0x40 && b15 == 0x81:
                return new PacketEventClassification(
                    "client.item.move", 1.0, "handler signature 08 40 81", true);

            case 0x18:
                return new PacketEventClassification(
                    "client.item.use", 1.0, "ClientConnection case 0x18 fallback", true);

            case 0x1A when b13 == 0x08 && b14 == 0x40 && b15 == 0x43:
                return new PacketEventClassification(
                    "client.chat.send", 1.0, "handler signature 08 40 43", true);

            case 0x1A when b13 == 0x08 && b14 == 0x40 && b15 == 0xC1:
                return new PacketEventClassification(
                    "client.item.pickup_to_slot", 1.0, "handler signature 08 40 C1", true);

            case 0x1A when b13 == 0x5C && b14 == 0x46 && b15 == 0xE1:
                return new PacketEventClassification(
                    "client.container.open_loot", 1.0, "handler signature 5C 46 E1", true);

            case 0x2D when b13 == 0x08 && b14 == 0x40 && b15 == 0x63:
                return new PacketEventClassification(
                    "client.item.drop", 1.0, "handler signature 08 40 63", true);

            case 0x31 or 0x36 when b13 == 0x08 && b14 == 0x40 && b15 == 0xA3:
                return new PacketEventClassification(
                    "client.npc.interact", 1.0, "NPC signature 08 40 A3", true);

            case 0x15 or 0x1B or 0x1F or 0x23
                when b13 == 0x08 && b14 == 0x40 && b15 is 0xA3 or 0x83:
                return new PacketEventClassification(
                    "client.item.take_mainhand", 1.0,
                    b15 == 0x83 ? "handler signature 08 40 83" : "handler signature 08 40 A3", true);

            case 0x19 or 0x20 when b13 == 0x08 && b14 == 0x40 && b15 == 0x03:
                return new PacketEventClassification(
                    "client.trade.buy", 1.0, "handler signature 08 40 03", true);

            case 0x19 or 0x20:
                return new PacketEventClassification(
                    "client.combat.damage_target", 1.0, "ClientConnection damage fallback", true);
        }

        // Signature-only fallbacks when the length byte is unexpected but payload matches.
        if (b13 == 0x08 && b14 == 0x40)
        {
            return b15 switch
            {
                0x83 => new PacketEventClassification(
                    "client.item.take_mainhand", 1.0, "handler signature 08 40 83", true),
                0xA3 => new PacketEventClassification(
                    "client.item.take_mainhand", 1.0, "handler signature 08 40 A3", true),
                0x43 => new PacketEventClassification(
                    "client.chat.send", 0.9, "handler signature 08 40 43", true),
                0x63 => new PacketEventClassification(
                    "client.item.drop", 0.9, "handler signature 08 40 63", true),
                0x23 => new PacketEventClassification(
                    "client.group.action", 0.9, "handler signature 08 40 23", true),
                0x81 => new PacketEventClassification(
                    "client.item.move", 0.9, "handler signature 08 40 81", true),
                0xC1 => new PacketEventClassification(
                    "client.item.pickup_to_slot", 0.9, "handler signature 08 40 C1", true),
                0x03 => new PacketEventClassification(
                    "client.trade.buy", 0.9, "handler signature 08 40 03", true),
                _ => new PacketEventClassification(
                    "client.unknown", 0, "no known handler signature", false)
            };
        }

        if (b13 == 0x5C && b14 == 0x46 && b15 == 0xE1)
        {
            return new PacketEventClassification(
                "client.container.open_loot", 0.9, "handler signature 5C 46 E1", true);
        }

        return new PacketEventClassification(
            "client.unknown", 0, "no known handler signature", false);
    }

    public static PacketEventClassification ClassifyServerAck()
    {
        return new PacketEventClassification("server.protocol.ack", 1.0, "no entity header", true);
    }

    public static PacketEventClassification ClassifyServerKeepalivePong()
    {
        return new PacketEventClassification(
            "server.protocol.keepalive_pong",
            1.0,
            "PingHandler response to client 0x26 keepalive",
            true);
    }

    public static PacketEventClassification ClassifyServerSixSecondPing()
    {
        return new PacketEventClassification(
            "server.protocol.ping_6s",
            1.0,
            "CommonPackets.SixSecondPing",
            true);
    }

    public static PacketEventClassification ClassifyServerFifteenSecondPing()
    {
        return new PacketEventClassification(
            "server.protocol.ping_15s",
            1.0,
            "CommonPackets.FifteenSecondPing",
            true);
    }

    public static PacketEventClassification ClassifyFalseBoundary(int reservedLow, bool reservedBit28)
    {
        var reservedBit = reservedBit28 ? 1 : 0;
        return new PacketEventClassification(
            "server.parser.false_boundary",
            1.0,
            $"reserved bits are {reservedLow}/{reservedBit}",
            false);
    }

    public static PacketEventClassification ClassifyUnresolvedAction(byte actionType)
    {
        return new PacketEventClassification(
            "server.unresolved_header_candidate",
            0.15,
            $"reserved bits match, but action {actionType} is not identified",
            false);
    }

    public static PacketEventClassification ClassifyServerEntity(
        ObjectType objectType,
        ushort objectTypeVal,
        EntityActionType actionType,
        byte actionTypeVal,
        bool parseSuccess,
        bool actionRecovered)
    {
        if (objectType == ObjectType.Despawn)
        {
            return new PacketEventClassification("server.entity.despawn", 1.0, "despawn definition", true);
        }

        if (actionType == EntityActionType.ATTACK)
        {
            return new PacketEventClassification("server.combat.damage", 1.0, "EntityActionType.ATTACK", true);
        }

        if (actionType == EntityActionType.SET_POSITION)
        {
            return new PacketEventClassification("server.entity.position", 1.0, "EntityActionType.SET_POSITION", true);
        }

        if (actionType == EntityActionType.INTERACT)
        {
            if (objectTypeVal == 1)
            {
                return new PacketEventClassification(
                    "server.system.interaction_type_1", 1.0, "object_type=1 action_type=10", true);
            }

            return new PacketEventClassification(
                "server.entity.interaction", 0.8, "EntityActionType.INTERACT", true);
        }

        if (actionType is EntityActionType.FULL_SPAWN or EntityActionType.FULL_SPAWN_2)
        {
            var spawnName = objectType == ObjectType.Unknown
                ? $"server.entity.spawn.object_{objectTypeVal}"
                : $"server.entity.spawn.{ToSnakeCase(objectType.ToString())}";
            var confidence = parseSuccess ? 1.0 : actionRecovered ? 0.95 : 0.5;
            var reason = actionRecovered
                ? "action recovered from raw header"
                : parseSuccess
                    ? "confirmed by existing PacketParts comment"
                    : "full spawn with partial definition";
            return new PacketEventClassification(spawnName, confidence, reason, true);
        }

        if (actionType == EntityActionType.UNKNOWN)
        {
            return new PacketEventClassification(
                "server.entity.state_variant", 0.5, "EntityActionType.UNKNOWN (0x14)", true);
        }

        if (actionType == EntityActionType.UNDEF)
        {
            return ClassifyUnresolvedAction(actionTypeVal);
        }

        return new PacketEventClassification(
            "server.protocol_or_payload", 0.25, "no entity header", false);
    }

    public static PacketTypes? ToPacketType(string eventName)
    {
        return eventName switch
        {
            "client.position_keepalive" => PacketTypes.CLIENT_PING,
            "client.chat.send" => PacketTypes.CLIENT_SEND_CHAT_MESSAGE,
            "client.combat.damage_target" => PacketTypes.CLIENT_ATTACK_TARGET,
            "client.item.move" => PacketTypes.CLIENT_MOVE_ITEM,
            "server.protocol.ack" => PacketTypes.SERVER_CONNECTION_ACCEPTED,
            "server.protocol.keepalive_pong" => PacketTypes.SERVER_PING_6_SEC,
            "server.protocol.ping_6s" => PacketTypes.SERVER_PING_6_SEC,
            "server.protocol.ping_15s" => PacketTypes.SERVER_PING_15_SEC,
            "server.entity.despawn" => PacketTypes.SERVER_DESPAWN_ENTITY,
            "server.entity.position" => PacketTypes.SERVER_MOVE_ENTITY,
            _ when eventName.StartsWith("server.entity.spawn.", StringComparison.Ordinal)
                => PacketTypes.SERVER_NEW_OBJECT,
            _ => null
        };
    }

    private static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
            {
                sb.Append('_');
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }
}
