namespace SphServer.Shared.GameData.Enums;

/// <summary>
/// Underlying values stay unique so a switch on NpcType stays legal
/// </summary>
public enum NpcType
{
    TradeMagic = 9,
    TradeAlchemy = 6,
    TradeWeapon = 11,
    TradeJewelry = 8,
    TradeArmor = 7,
    TradeTravelGeneric = 10,
    TradeTravelTokens = 12,
    TradeTavernkeeper = 5,
    QuestTitle = 13,
    QuestDegree = 14,
    QuestKarma = 15,
    Guilder = 16,
    Banker = 17,
    Prefix = 18,
    Tournament = 19
}
