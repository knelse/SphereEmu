using System;
using System.Collections.Generic;
using System.Linq;
using SpherePacketVisualEditor;
using SphServer.Helpers;

namespace PacketLogViewer.Models.PacketAnalyzeData;

public class ItemPacket : PacketAnalyzeData
{
    public EntityActionType ActionType { get; set; } = EntityActionType.UNDEF;
    public bool HasGameId { get; set; }
    public int GameObjectId { get; set; }
    public int ContainerId { get; set; }
    public int Count { get; set; } = 1;
    public ItemSuffix ItemSuffix { get; set; } = ItemSuffix.None;
    public int PALevel { get; set; }
    public int RemainingUses { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public bool HasSuffix { get; set; }
    public int Suffix { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public int Angle { get; set; }

    public readonly SphGameObject? GameObject;

    public readonly string OverrideType = string.Empty;

    public override string DisplayValue => GetDisplayValue();

    public ItemPacket (List<PacketPart> parts) : base(parts)
    {
        var actionTypePart = Parts.FirstOrDefault(x => x.Name == PacketPartNames.ActionType);
        if (actionTypePart is not null)
        {
            var actionTypeVal = (int) (actionTypePart.ActualLongValue ?? int.MaxValue);
            ActionType = Enum.IsDefined(typeof (EntityActionType), actionTypeVal)
                ? (EntityActionType) actionTypeVal
                : EntityActionType.UNDEF;
        }

        if (ActionType is EntityActionType.FULL_SPAWN or EntityActionType.FULL_SPAWN_2)
        {
            HasGameId = GetBitValue(PacketPartNames.HasGameId);
            GameObjectId = GetIntValue(PacketPartNames.GameObjectId);
            // 0xFF00 = on the ground
            ContainerId = GetIntValue(PacketPartNames.ContainerId);
            Count = Math.Max(GetIntValue(PacketPartNames.Count), 1);
            PALevel = GetIntValue(PacketPartNames.PALevel);
            RemainingUses = GetIntValue(PacketPartNames.RemainingUses);
            OwnerName = GetStringValue(PacketPartNames.OwnerName);
            HasSuffix = !GetBitValue(PacketPart.HasSuffixValue);
            Suffix = GetIntValue(PacketPartNames.Suffix);
            X = GetClientCoordValue(PacketPartNames.CoordX);
            Y = GetClientCoordValue(PacketPartNames.CoordY);
            Z = GetClientCoordValue(PacketPartNames.CoordZ);
            Angle = GetIntValue(PacketPartNames.Angle);

            if (HasGameId)
            {
                GameObject = SphObjectDb.GameObjectDataDb[GameObjectId];
            }

            var subtypeId = GetIntValue(PacketPartNames.SubtypeId);

            if (ObjectType is ObjectType.ScrollLegend or ObjectType.ScrollRecipe)
            {
                var scrollName = $"scroll{subtypeId:000}";
                if (SphObjectDb.LocalisationContent.ContainsKey(scrollName))
                {
                    var localized = SphObjectDb.LocalisationContent[scrollName][Locale.Russian];
                    if (localized.Length > 0)
                    {
                        OverrideType = localized[0][3..];
                    }
                }
            }
            else if (ObjectType is ObjectType.Key or ObjectType.KeyBarn or ObjectType.DoorEntrance)
            {
                var keyLocales = SphObjectDb.LocalisationContent["st_key"][Locale.Russian];
                var subtypeStr = $"{subtypeId}";
                var text = keyLocales.FirstOrDefault(x => x.StartsWith(subtypeStr));
                if (!string.IsNullOrEmpty(text))
                {
                    OverrideType = text[(subtypeStr.Length + 1)..];
                }
            }
            else if (ObjectType is ObjectType.Token or ObjectType.TokenMultiuse)
            {
                var keyLocales = SphObjectDb.LocalisationContent["_tokens"][Locale.Russian];
                var subtypeStr = $"{subtypeId}";
                var text = keyLocales.FirstOrDefault(x => x.StartsWith(subtypeStr));
                if (!string.IsNullOrEmpty(text))
                {
                    var remainingStr = ObjectType is ObjectType.TokenMultiuse && RemainingUses > 0
                        ? $" ({RemainingUses})"
                        : string.Empty;
                    OverrideType = "Жетон ТП, " + text[(subtypeStr.Length + 1)..] + remainingStr;
                }
            }
            else if (ObjectType is ObjectType.TokenIslandGuest)
            {
                var ownerStr = string.IsNullOrEmpty(OwnerName) ? string.Empty : $" ({OwnerName})";
                OverrideType = "Гостевой жетон на ЛО" + ownerStr;
            }
        }
    }

    private string GetDisplayValue ()
    {
        var typeName = $"({Enum.GetName(ObjectType)!})";
        string tier;
        var displayName =
            HasGameId
                ? SphObjectDb.GameObjectDataDb[GameObjectId].Localisation[Locale.Russian]
                : string.IsNullOrEmpty(OverrideType)
                    ? ObjectPacketTools.GetFriendlyNameByObjectType(ObjectType)
                    : OverrideType;

        if (GameObject?.GameObjectType == GameObjectType.Ring)
        {
            tier = GameObject.TitleMinusOne > 0
                ? $"{GameObject.TitleMinusOne + 1}т"
                : GameObject.DegreeMinusOne > 0
                    ? $"{GameObject.DegreeMinusOne + 1}с"
                    : GameObject.ToRomanTierLiteral();
        }
        else
        {
            tier = GameObject?.ToRomanTierLiteral() ?? string.Empty;
        }

        var suffixLocale = string.Empty;
        if (GameObject is not null)
        {
            if (HasSuffix)
            {
                if (GameObjectDataHelper.ObjectTypeToSuffixLocaleMapActual.ContainsKey(GameObject!.GameObjectType) &&
                    GameObjectDataHelper.ObjectTypeToSuffixLocaleMapActual[GameObject!.GameObjectType]
                        .Any(x => x.Value.value == Suffix))
                {
                    GameObject.Suffix = GameObjectDataHelper
                        .ObjectTypeToSuffixLocaleMapActual[GameObject!.GameObjectType]
                        .First(x => x.Value.value == Suffix).Key;
                }
                else
                {
                    Console.WriteLine($"No suffix for {GameObject.GameObjectType} and ID {Suffix}");
                }

                if (SphObjectDb.LocalisationContent.ContainsKey(GameObject.SphereType))
                {
                    var localeEntries = SphObjectDb.LocalisationContent[GameObject.SphereType][Locale.Russian];
                    var suffixStr = $"2{Suffix:00}";
                    var suffixLocaleStr = localeEntries.FirstOrDefault(x => x.StartsWith(suffixStr));
                    if (!string.IsNullOrEmpty(suffixLocaleStr))
                    {
                        suffixLocale = $"{suffixLocaleStr[3..]}";
                    }
                }
            }
        }

        var count = Count > 1 ? $" ({Count})" : string.Empty;
        var name = $"{displayName}" + suffixLocale +
                   (string.IsNullOrEmpty(tier) ? tier : $" {tier}") + count;
        var pa = string.Empty;
        if (PALevel > 0)
        {
            pa = $" PA: {PALevel}";
        }

        return $"{name,-44}ID: {Id:X4}  GMID: {GameObjectId.ToString(),5}  " +
               $"Type: {(int) ObjectType,4} {typeName,-24} Suff: N/A  Bag: {ContainerId:X4}{pa}";
    }
}