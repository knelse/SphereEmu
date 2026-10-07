using LiteDB;

namespace SphServer.Shared.Db;

/// <summary>
/// LiteDB names Chest, Firework, and WeaponSword do not match ObjectType, and a throw here skips
/// TCP accept
/// </summary>
public static class ObjectTypeBson
{
    private static readonly Dictionary<string, ObjectType> OldStoredNames =
        new (StringComparer.OrdinalIgnoreCase)
        {
            ["Chest"] = ObjectType.Chest2,
            ["Firework"] = ObjectType.Firework_Celebration,
        };

    public static void Register ()
    {
        BsonMapper.Global.RegisterType<ObjectType> (
            type => new BsonValue ((int) type),
            Parse);
        BsonMapper.Global.RegisterType<ObjectType?> (
            type => type.HasValue ? new BsonValue ((int) type.Value) : BsonValue.Null,
            bson => bson.IsNull ? null : Parse (bson));
    }

    public static ObjectType Parse (BsonValue bson)
    {
        if (bson is null || bson.IsNull)
        {
            return ObjectType.Unknown;
        }

        if (bson.IsNumber)
        {
            var n = bson.AsInt32;
            return n is >= 0 and <= ushort.MaxValue && Enum.IsDefined (typeof (ObjectType), (ushort) n)
                ? (ObjectType) n
                : ObjectType.Unknown;
        }

        if (!bson.IsString)
        {
            return ObjectType.Unknown;
        }

        var name = bson.AsString;
        if (OldStoredNames.TryGetValue (name, out var remapped))
        {
            return remapped;
        }

        return ObjectTypeParse.TryParse (name, out var parsed) ? parsed : ObjectType.Unknown;
    }
}
