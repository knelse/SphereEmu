using System.Collections.Generic;
using Godot;
using SphServer.Packets;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking.WorldObject.Serializers;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class NpcInteractable : WorldObject
{
	[Export] public int NameID { get; set; } = 4016;

	protected override bool RefreshModelVisualOnReady => true;
	protected override bool AutoGroundGlbVisual => true;

	public string ModelNameSph => ModelName + "\0";
	[Export] public string IconName { get; set; } = string.Empty;
	public string IconNameSph => IconName + "\0";
	public int IconNameLength => IconNameSph.Length;

	[Export] public NpcType NpcType { get; set; }
	[Export] public int VendorItemTierMin { get; set; }
	[Export] public int VendorItemTierMax { get; set; }
	[Export] public VendorLocation VendorLocation { get; set; }
	public readonly List<ItemDbEntry> ItemsOnSale = [];

	/// <summary>Client vendor UI only shows this many slots.</summary>
	public const int MaxDisplayedShopItems = 74;

	private NpcInteractableSerializer? serializer;

	public override void _Ready()
	{
		base._Ready();
		if (Engine.IsEditorHint())
		{
			return;
		}

		ObjectType = NpcType switch
		{
			NpcType.Banker => ObjectType.Npc_Banker,
			NpcType.Guilder => ObjectType.Npc_Guilder,
			NpcType.QuestTitle => ObjectType.Npc_Quest_Title,
			NpcType.QuestKarma => ObjectType.Npc_Quest_Karma,
			NpcType.QuestDegree => ObjectType.Npc_Quest_Degree,
			NpcType.Tournament => ObjectType.Npc_Guilder,
			_ => ObjectType.Npc_Trade
		};

		if (IsTradeNpc() && (VendorItemTierMax == 0 || VendorItemTierMin == 0))
		{
			SphLogger.Warning($"Vendor [{ID}] ({NpcType}) has no item tiers set");
		}

		serializer = new NpcInteractableSerializer(this);
	}

	public void ClientInteraction(ushort clientID,
		ClientInteractionType interactionType = ClientInteractionType.Unknown)
	{
		ClientInteract(clientID, interactionType);
	}

	private static bool IsTradeNpc(NpcType npcType)
	{
		return npcType is NpcType.TradeJewelry or NpcType.TradeTravelGeneric or NpcType.TradeWeapon
			or NpcType.TradeArmor or NpcType.TradeAlchemy or NpcType.TradeMagic or NpcType.TradeTravelTokens
			or NpcType.TradeTavernkeeper;
	}

	private bool IsTradeNpc()
	{
		return IsTradeNpc(NpcType);
	}
}
