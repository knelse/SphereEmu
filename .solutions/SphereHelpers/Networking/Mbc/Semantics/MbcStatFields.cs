namespace SphServer.Helpers.Networking;

/// <summary>
///     g_rec_0C44 field index names. Same markers as the 08 C0 stat stream
///     (stat_field_markers.sphenum / NetworkedStatsUpdater).
///     Edit here when a marker is identified.
/// </summary>
public static class MbcStatFields
{
    public static readonly IReadOnlyDictionary<int, string> Names = new Dictionary<int, string>
    {
        [0] = "hp_current",
        [1] = "hp_max",
        [2] = "mp_current",
        [3] = "mp_max",
        [4] = "satiety_current",
        [5] = "satiety_max",
        [6] = "strength",
        [7] = "agility",
        [8] = "accuracy",
        [9] = "endurance",
        [10] = "earth",
        [11] = "air",
        [12] = "water",
        [13] = "fire",
        [16] = "pd",
        [17] = "md",
        [18] = "pa",
        [19] = "ma",
        [20] = "specab",
        [21] = "block_equip",
        [22] = "is_invisible",
        [37] = "title_level",
        [38] = "degree_level",
        [39] = "karma_type",
        [40] = "karma",
        [41] = "title_xp",
        [42] = "degree_xp",
        [43] = "stats_available",
        [44] = "title_stats_available",
        [45] = "degree_stats_available",
        [46] = "gender",
        [47] = "clan_rank_type",
        [52] = "title_rebirth",
        [53] = "degree_rebirth",
        [57] = "money",
    };

    public static readonly string[] SkillAdds =
    [
        "strength+", "agility+", "accuracy+", "endurance+",
        "earth+", "air+", "water+", "fire+"
    ];

    public static string Name(int index) =>
        Names.TryGetValue(index, out var name) ? name : $"stat.i{index}";
}
