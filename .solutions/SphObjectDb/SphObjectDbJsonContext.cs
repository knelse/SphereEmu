using System.Text.Json.Serialization;

[JsonSourceGenerationOptions (WriteIndented = true)]
[JsonSerializable (typeof (Dictionary<int, SphGameObject>))]
[JsonSerializable (typeof (Dictionary<string, Dictionary<Locale, string[]>>))]
[JsonSerializable (typeof (Dictionary<string, Dictionary<int, Dictionary<Locale, string>>>))]
[JsonSerializable (typeof (Dictionary<GameObjectType, Dictionary<ItemSuffix, SphGameObject>>))]
internal partial class SphObjectDbJsonContext : JsonSerializerContext;
