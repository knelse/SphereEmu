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
	NpcInteract,
	ItemTakeMainhand,
	TradeBuy,
	CombatDamageTarget
}

public readonly record struct ClientPacketClassification(
	ClientPacketEvent Event,
	string EventName,
	double Confidence,
	string Reason,
	bool IsEvent);

/// <summary>
///     Classifies decoded client frames by length prefix + handler signature bytes.
///     Shared by the emu dispatch path and PacketLogViewer.
/// </summary>
public static class ClientPacketClassifier
{
	public static ClientPacketClassification ClassifyFrame(ReadOnlySpan<byte> frame)
	{
		if (frame.Length == 0)
		{
			return Result(ClientPacketEvent.InvalidOrTrailing, 0, "empty frame", false);
		}

		var declaredLength = frame[0];
		if (declaredLength != frame.Length)
		{
			// Some NPC frames are accepted with a non-canonical first-byte length.
			if (frame.Length >= 16 &&
				frame[13] == 0x08 && frame[14] == 0x40 && frame[15] == 0xA3 &&
				declaredLength is 0x31 or 0x36)
			{
				return Result(
					ClientPacketEvent.NpcInteract,
					1.0,
					"NPC signature 08 40 A3; noncanonical length",
					true);
			}

			return Result(ClientPacketEvent.InvalidOrTrailing, 0, "frame length is invalid", false);
		}

		if (declaredLength == 0x26)
		{
			return Result(ClientPacketEvent.PositionKeepalive, 1.0, "ClientConnection case 0x26", true);
		}

		if (declaredLength is 0x08 or 0x0C || frame.Length <= 12)
		{
			return Result(ClientPacketEvent.ProtocolControl, 0.75, "short control frame", true);
		}

		if (frame.Length < 16)
		{
			return Result(ClientPacketEvent.Unknown, 0, "no known handler signature", false);
		}

		var b13 = frame[13];
		var b14 = frame[14];
		var b15 = frame[15];

		switch (declaredLength)
		{
			case 0x13 when b13 == 0x08 && b14 == 0x40 && b15 == 0x23 &&
						   frame.Length > 16 && frame[16] == 0x23:
				return Result(ClientPacketEvent.GroupAction, 1.0, "handler signature 08 40 23", true);

			case 0x16 when b13 == 0x08 && b14 == 0x40 && b15 == 0x23:
				return Result(ClientPacketEvent.ItemPickup, 1.0, "handler signature 08 40 23", true);

			case 0x18 when b13 == 0x08 && b14 == 0x40 && b15 == 0x81:
				return Result(ClientPacketEvent.ItemMove, 1.0, "handler signature 08 40 81", true);

			case 0x18:
				return Result(ClientPacketEvent.ItemUse, 1.0, "ClientConnection case 0x18 fallback", true);

			case 0x1A when b13 == 0x08 && b14 == 0x40 && b15 == 0x43:
				return Result(ClientPacketEvent.ChatSend, 1.0, "handler signature 08 40 43", true);

			case 0x1A when b13 == 0x08 && b14 == 0x40 && b15 == 0xC1:
				return Result(ClientPacketEvent.ItemPickupToSlot, 1.0, "handler signature 08 40 C1", true);

			case 0x1A when b13 == 0x5C && b14 == 0x46 && b15 == 0xE1:
				return Result(ClientPacketEvent.ContainerOpenLoot, 1.0, "handler signature 5C 46 E1", true);

			case 0x2D when b13 == 0x08 && b14 == 0x40 && b15 == 0x63:
				return Result(ClientPacketEvent.ItemDrop, 1.0, "handler signature 08 40 63", true);

			case 0x31 or 0x36 when b13 == 0x08 && b14 == 0x40 && b15 == 0xA3:
				return Result(ClientPacketEvent.NpcInteract, 1.0, "NPC signature 08 40 A3", true);

			case 0x15 or 0x1B or 0x1F or 0x23
				when b13 == 0x08 && b14 == 0x40 && b15 is 0xA3 or 0x83:
				return Result(
					ClientPacketEvent.ItemTakeMainhand,
					1.0,
					b15 == 0x83 ? "handler signature 08 40 83" : "handler signature 08 40 A3",
					true);

			case 0x19 or 0x20 when b13 == 0x08 && b14 == 0x40 && b15 == 0x03:
				return Result(ClientPacketEvent.TradeBuy, 1.0, "handler signature 08 40 03", true);

			case 0x19 or 0x20:
				return Result(
					ClientPacketEvent.CombatDamageTarget,
					1.0,
					"ClientConnection damage fallback",
					true);
		}

		// Signature-only fallbacks when the length byte is unexpected but payload matches.
		if (b13 == 0x08 && b14 == 0x40)
		{
			return b15 switch
			{
				0x83 => Result(ClientPacketEvent.ItemTakeMainhand, 1.0, "handler signature 08 40 83", true),
				0xA3 => Result(ClientPacketEvent.ItemTakeMainhand, 1.0, "handler signature 08 40 A3", true),
				0x43 => Result(ClientPacketEvent.ChatSend, 0.9, "handler signature 08 40 43", true),
				0x63 => Result(ClientPacketEvent.ItemDrop, 0.9, "handler signature 08 40 63", true),
				0x23 => Result(ClientPacketEvent.GroupAction, 0.9, "handler signature 08 40 23", true),
				0x81 => Result(ClientPacketEvent.ItemMove, 0.9, "handler signature 08 40 81", true),
				0xC1 => Result(ClientPacketEvent.ItemPickupToSlot, 0.9, "handler signature 08 40 C1", true),
				0x03 => Result(ClientPacketEvent.TradeBuy, 0.9, "handler signature 08 40 03", true),
				_ => Result(ClientPacketEvent.Unknown, 0, "no known handler signature", false)
			};
		}

		if (b13 == 0x5C && b14 == 0x46 && b15 == 0xE1)
		{
			return Result(ClientPacketEvent.ContainerOpenLoot, 0.9, "handler signature 5C 46 E1", true);
		}

		return Result(ClientPacketEvent.Unknown, 0, "no known handler signature", false);
	}

	/// <summary>
	///     Walks length-prefixed frames in a decoded client payload.
	///     Chat continuations may follow a 0x1A chat header outside that frame's declared length —
	///     callers that dispatch chat should keep the full buffer available to the handler.
	/// </summary>
	public static List<(int Offset, int Length, ClientPacketClassification Classification)> EnumerateFrames(
		ReadOnlySpan<byte> content)
	{
		var frames = new List<(int Offset, int Length, ClientPacketClassification Classification)>();
		var offset = 0;
		while (offset < content.Length)
		{
			var declaredLength = content[offset];
			if (declaredLength >= 1 && offset + declaredLength <= content.Length)
			{
				frames.Add((offset, declaredLength, ClassifyFrame(content.Slice(offset, declaredLength))));
				offset += declaredLength;
				continue;
			}

			frames.Add((offset, content.Length - offset, ClassifyFrame(content.Slice(offset))));
			break;
		}

		return frames;
	}

	public static string ToEventName(ClientPacketEvent packetEvent) =>
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
			ClientPacketEvent.NpcInteract => "client.npc.interact",
			ClientPacketEvent.ItemTakeMainhand => "client.item.take_mainhand",
			ClientPacketEvent.TradeBuy => "client.trade.buy",
			ClientPacketEvent.CombatDamageTarget => "client.combat.damage_target",
			_ => "client.none"
		};

	private static ClientPacketClassification Result(
		ClientPacketEvent packetEvent,
		double confidence,
		string reason,
		bool isEvent) =>
		new(packetEvent, ToEventName(packetEvent), confidence, reason, isEvent);
}
