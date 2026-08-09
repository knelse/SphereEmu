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
	ItemSwap
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

		// 16-bit: chat text parts run past 255 bytes, and a one-byte read calls them invalid.
		var declaredLength = frame[0] | (frame[1] << 8);
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

		// Keepalive only when it does not carry the swap signature: the equipment-slot swap is the
		// same declared length, and returning here on length alone hid it among tens of thousands
		// of keepalives. The test is the whole signature, because releasing on 08 40 alone drops
		// every other 0x26 frame into the signature-only fallback, which dispatches it as a drop.
		if (declaredLength == 0x26 &&
			!(frame.Length > 15 && frame[13] == 0x08 && frame[14] == 0x40 && frame[15] == 0xA3))
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

			// The opcode is not byte-aligned: it is the 12 bits at 116, always E14, which is byte 15
			// plus only the HIGH nibble of byte 14. Byte 13 and byte 14's low nibble carry item data
			// and vary per item, so matching either whole byte drops every item but the one it was
			// read from. The item's id is at bytes 11-12, where UseItemHandler reads it.
			case 0x15 when b15 == 0xE1 && (b14 & 0xF0) == 0x40:
				return Result(ClientPacketEvent.ItemUse, 1.0, "use opcode E14", true);

			case 0x25 when b13 == 0x08 && b14 == 0x40 && b15 == 0x63:
				// Shares 08 40 63 with the drop frame; only the length separates them.
				return Result(ClientPacketEvent.ItemDragOnGround, 1.0, "handler signature 08 40 63", true);

			case 0x2D when b13 == 0x08 && b14 == 0x40 && b15 == 0x63:
				return Result(ClientPacketEvent.ItemDrop, 1.0, "handler signature 08 40 63", true);

			case 0x31 or 0x36 when b13 == 0x08 && b14 == 0x40 && b15 == 0xA3:
				return Result(ClientPacketEvent.NpcInteract, 1.0, "NPC signature 08 40 A3", true);

			case 0x26 when b13 == 0x08 && b14 == 0x40 && b15 == 0xA3:
				// Dropping an item onto an occupied slot. Shares its length with the position frame,
				// which is why the keepalive check above reads the signature before claiming it.
				return Result(ClientPacketEvent.ItemSwap, 1.0, "swap signature 08 40 A3", true);

			case 0x15 or 0x1B or 0x1F or 0x23
				when b13 == 0x08 && b14 == 0x40 && b15 is 0xA3 or 0x83:
				return Result(
					ClientPacketEvent.ItemTakeMainhand,
					1.0,
					b15 == 0x83 ? "handler signature 08 40 83" : "handler signature 08 40 A3",
					true);

			case 0x19 when b13 == 0x08 && b14 == 0x40 && b15 == 0x83:
				// Switching to bare fists.
				return Result(ClientPacketEvent.ItemTakeMainhand, 1.0, "handler signature 08 40 83", true);

			case 0x19 or 0x20 when b13 == 0x08 && b14 == 0x40 && b15 == 0x03:
				return Result(ClientPacketEvent.TradeBuy, 1.0, "handler signature 08 40 03", true);

			case 0x19 or 0x20 or 0x2C when b13 == 0x08 && b14 == 0x40 && b15 == 0xA3 &&
										   frame[18] is 0xA1 or 0xA3 && frame[19] == 0x41:
				// Taking something in hand and putting it down are the same message; an attack is not.
				// Byte 18 straddles the record and its low bits sit ahead of the signature, one of
				// them set only when the hand is being emptied — so testing the whole byte dropped
				// that frame onto the damage path, where its 0xFFFF reads as "no target".
				return Result(ClientPacketEvent.ItemTakeMainhand, 1.0, "hand-change signature 41", true);

			case 0x2C when b13 == 0x08 && b14 == 0x40 && b15 == 0xA3:
				// Attacking with something in hand; the arm above claims taking one by its A1 41.
				return Result(ClientPacketEvent.CombatDamageTarget, 1.0, "armed attack 08 40 A3", true);

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
				// Trigger and text parts alike: the chat handler tells them apart and assembles.
				0x43 => Result(ClientPacketEvent.ChatSend, 1.0, "handler signature 08 40 43", true),
				0x83 => Result(ClientPacketEvent.ItemTakeMainhand, 1.0, "handler signature 08 40 83", true),
				0xA3 => Result(ClientPacketEvent.ItemTakeMainhand, 1.0, "handler signature 08 40 A3", true),
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
			ClientPacketEvent.ItemDragOnGround => "client.item.drag_on_ground",
			ClientPacketEvent.NpcInteract => "client.npc.interact",
			ClientPacketEvent.ItemTakeMainhand => "client.item.take_mainhand",
			ClientPacketEvent.TradeBuy => "client.trade.buy",
			ClientPacketEvent.CombatDamageTarget => "client.combat.damage_target",
			ClientPacketEvent.ItemSwap => "client.item.swap",
			_ => "client.none"
		};

	private static ClientPacketClassification Result(
		ClientPacketEvent packetEvent,
		double confidence,
		string reason,
		bool isEvent) =>
		new(packetEvent, ToEventName(packetEvent), confidence, reason, isEvent);
}
