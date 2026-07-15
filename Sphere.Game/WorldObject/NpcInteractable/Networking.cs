using SphServer.Packets;
using SphServer.Shared.GameData.Enums;
using SphServer.Sphere.Game.Converters;

namespace SphServer.Sphere.Game.WorldObject;

public partial class NpcInteractable
{
	protected override List<PacketPart> GetPacketParts()
	{
		return PacketPart.LoadDefinedPartsFromFile(NpcType);
	}

	protected override List<PacketPart> ModifyPacketParts(List<PacketPart> packetParts)
	{
		PacketPart.UpdateValue(packetParts, "name_id", NameID - 4000, 11);
		var modelName = ModelNameSph;
		if (NpcType is NpcType.Guilder)
		{
			modelName = modelName.PadRight(16, '\0');
		}

		PacketPart.UpdateValue(packetParts, "entity_type_name_length", modelName.Length, 8);
		PacketPart.UpdateValue(packetParts, "entity_type_name", modelName);
		PacketPart.UpdateValue(packetParts, "icon_name_length", IconNameLength, 8);
		PacketPart.UpdateValue(packetParts, "icon_name", IconNameSph);
		var tradeType = NpcTypeToNpcTradeTypeSph.Convert(NpcType);
		PacketPart.UpdateValue(packetParts, "npc_trade_type", tradeType, 4);
		return packetParts;
	}
}
