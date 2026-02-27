public enum ClanRank : byte
{
    Senior = 0x0,
    Seneschal = 0x1,
    Vassal = 0x2,
    Neophyte = 0x3,

    // not saved in game
    Candidate = 0x4,
    Kicked = 0x5,

    // breaks client if used after clan join
    Accepted = 0x6
}