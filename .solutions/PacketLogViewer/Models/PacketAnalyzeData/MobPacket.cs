using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB;
using SpherePacketVisualEditor;
using SphServer.Helpers;

namespace PacketLogViewer.Models.PacketAnalyzeData;

public class MobPacket : PacketAnalyzeData
{
    [BsonId] public int DbId { get; set; }
    public EntityActionType ActionType { get; set; } = EntityActionType.UNDEF;
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public int Angle { get; set; }
    public int Level { get; set; }
    public int CurrentHP { get; set; }
    public int MaxHP { get; set; }
    public int Type { get; set; }

    public override string DisplayValue =>
        $"{Id:X4} ({Enum.GetName(ObjectType) ?? string.Empty}) {TypenameDisplayValue} {LevelAndHpDisplayValue}at [{X:F2}, {Y:F2}, {Z:F2}]";

    private string LevelAndHpDisplayValue => Level == 0 ? string.Empty : $"lvl {Level} {CurrentHP}/{MaxHP} ";

    private string TypenameDisplayValue =>
        Type == 0
            ? string.Empty
            : SphObjectDb.GameObjectDataDb.ContainsKey(Type)
                ? SphObjectDb.GameObjectDataDb[Type].Localisation[Locale.Russian]
                : string.Empty;

    public MobPacket (List<PacketPart> parts) : base(parts)
    {
        var actionTypePart = Parts.FirstOrDefault(x => x.Name == PacketPartNames.ActionType);
        if (actionTypePart is not null)
        {
            var actionTypeVal = (int) (actionTypePart.ActualLongValue ?? int.MaxValue);
            ActionType = Enum.IsDefined(typeof (EntityActionType), actionTypeVal)
                ? (EntityActionType) actionTypeVal
                : EntityActionType.UNDEF;
        }

        if (ActionType is EntityActionType.SET_POSITION or EntityActionType.FULL_SPAWN)
        {
            X = GetClientCoordValue(PacketPartNames.CoordX);
            Y = GetClientCoordValue(PacketPartNames.CoordY);
            Z = GetClientCoordValue(PacketPartNames.CoordZ);
            Angle = GetIntValue(PacketPartNames.Angle);
        }

        if (ActionType is EntityActionType.FULL_SPAWN && ObjectType != ObjectType.MobSpawner)
        {
            Type = GetIntValue(PacketPartNames.MobType);
            CurrentHP = GetIntValue(PacketPartNames.CurrentHP);
            MaxHP = GetIntValue(PacketPartNames.MaxHP);
            Level = GetIntValue(PacketPartNames.Level);
        }
    }
}