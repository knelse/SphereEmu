using SphServer.Shared.GameData.Enums;

namespace SphServer.Sphere.Game.IngameToEmulatorTypeConverters;

public static class NpcTypeToNpcTradeTypeSph
{
    public static int Convert (NpcType npcType)
    {
        return npcType switch
        {
            NpcType.TradeMagic => 9,
            NpcType.TradeAlchemy => 6,
            NpcType.TradeWeapon => 11,
            NpcType.TradeJewelry => 8,
            NpcType.TradeArmor => 7,
            NpcType.TradeTavernkeeper => 5,
            NpcType.TradeTravelGeneric => 10,
            NpcType.TradeTravelTokens => 10,
            NpcType.QuestTitle => 4,
            NpcType.QuestDegree => 2,
            NpcType.QuestKarma => 3,
            NpcType.Guilder => 1,
            NpcType.Banker => 0,
            NpcType.Prefix => 12,
            NpcType.Tournament => 13,
            _ => 0
        };
    }
}