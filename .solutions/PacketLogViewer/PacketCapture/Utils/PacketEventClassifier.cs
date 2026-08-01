using System;
using System.Text;
using SphServer.Helpers;
using SphServer.Helpers.Networking;

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
        var classification = ClientPacketClassifier.ClassifyFrame(frame);
        return ToPacketEventClassification(classification);
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

    private static PacketEventClassification ToPacketEventClassification(
        ClientPacketClassification classification) =>
        new(classification.EventName, classification.Confidence, classification.Reason, classification.IsEvent);

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
