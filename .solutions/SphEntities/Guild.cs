public enum Guild : byte
{
    None = 0x0,
    Assasin = 0x1,
    Crusader = 0x2,
    Inquisitor = 0x3,
    Hunter = 0x4,
    Archmage = 0x5,
    Barbarian = 0x6,
    Druid = 0x7,
    Thief = 0x8,
    MasterOfSteel = 0x9,
    Armorer = 0x10,
    Blacksmith = 0x11,
    Warlock = 0x12,
    Necromancer = 0x13,
    Bandier = 0x14
}

public enum GuildRank : byte
{
    Candidate = 0x0,
    Scholar = 0x1,
    Apprentice = 0x2,
    Master = 0x3,
    Elder = 0x4,
    Expert = 0x5,
}

/// <summary>
///     <c>group_guilds.cfg</c> extra column: <c>+A0</c> = Assassin rank 0, <c>+A</c> = same as <c>+A0</c>.
///     Letters A–N follow client guild order, not the hex gaps in <see cref="Guild"/>.
/// </summary>
public static class GuildCatalog
{
    public static readonly Guild[] LetterOrder =
    [
        Guild.Assasin, Guild.Crusader, Guild.Inquisitor, Guild.Hunter, Guild.Archmage,
        Guild.Barbarian, Guild.Druid, Guild.Thief, Guild.MasterOfSteel, Guild.Armorer,
        Guild.Blacksmith, Guild.Warlock, Guild.Necromancer, Guild.Bandier
    ];

    public static bool TryParseRequirement(string? token, out Guild guild, out int rankMinusOne)
    {
        guild = Guild.None;
        rankMinusOne = 0;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        token = token.Trim();
        if (token is "-" || token[0] != '+' || token.Length is < 2 or > 3)
        {
            return false;
        }

        var index = char.ToUpperInvariant(token[1]) - 'A';
        if (index < 0 || index >= LetterOrder.Length)
        {
            return false;
        }

        if (token.Length == 3)
        {
            var digit = token[2] - '0';
            if (digit is < 0 or > (int)GuildRank.Expert)
            {
                return false;
            }

            rankMinusOne = digit;
        }

        guild = LetterOrder[index];
        return true;
    }

    // Display levels (minus-one + 1 in the 60-level cycle) required for ranks 1-6.
    private static readonly int[] RankMinDisplayLevel = [16, 21, 31, 41, 51, 60];
    private const int OffTrackMaxDisplayLevel = 15;
    private const int LevelsPerCycle = 60;

    public static bool MeetsRankRequirements(Guild guild, int rankMinusOne, int titleMinusOne, int degreeMinusOne)
    {
        if (guild == Guild.None)
        {
            return true;
        }

        if (rankMinusOne is < 0 or > (int)GuildRank.Expert)
        {
            return false;
        }

        var need = RankMinDisplayLevel[rankMinusOne];
        var title = DisplayLevelInCycle(titleMinusOne);
        var degree = DisplayLevelInCycle(degreeMinusOne);
        return LevelTrack(guild) switch
        {
            GuildLevelTrack.Title => title >= need && degree <= OffTrackMaxDisplayLevel,
            GuildLevelTrack.Degree => degree >= need && title <= OffTrackMaxDisplayLevel,
            _ => title >= need && degree >= need
        };
    }

    public static GuildLevelTrack LevelTrack(Guild guild) => guild switch
    {
        Guild.Crusader or Guild.Hunter or Guild.MasterOfSteel or Guild.Armorer or Guild.Bandier
            => GuildLevelTrack.Title,
        Guild.Inquisitor or Guild.Archmage or Guild.Druid or Guild.Warlock or Guild.Necromancer
            => GuildLevelTrack.Degree,
        _ => GuildLevelTrack.Both
    };

    private static int DisplayLevelInCycle(int minusOne) => minusOne % LevelsPerCycle + 1;
}

public enum GuildLevelTrack : byte
{
    Title,
    Degree,
    Both
}
