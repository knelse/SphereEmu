using System.Text.Json.Serialization;

namespace SphServer.Sphere.Game.Missions;

/// <summary>Mission categories used for XP award multipliers in <c>Config/Balance/experience.json</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MissionType
{
    [JsonStringEnumMemberName("mob_kill_in_dungeon")]
    MobKillInDungeon,

    [JsonStringEnumMemberName("mob_kill_outside")]
    MobKillOutside,

    [JsonStringEnumMemberName("parcel_from_dungeon")]
    ParcelFromDungeon,

    [JsonStringEnumMemberName("parcel_deliver_direct")]
    ParcelDeliverDirect,

    [JsonStringEnumMemberName("parcel_kill_and_deliver")]
    ParcelKillAndDeliver,

    [JsonStringEnumMemberName("npc_in_dungeon")]
    NpcInDungeon,

    [JsonStringEnumMemberName("npc_outside")]
    NpcOutside,

    [JsonStringEnumMemberName("money")]
    Money,

    [JsonStringEnumMemberName("item_collect_for_xp")]
    ItemCollectForXp,

    [JsonStringEnumMemberName("item_collect_for_item")]
    ItemCollectForItem
}
