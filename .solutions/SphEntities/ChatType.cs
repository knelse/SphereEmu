/// <summary>
/// Clan, group, PM and their say* versions 
/// </summary>
public enum PrivateChatType
{
    Clan = 4,
    Group = 5,
    SayClan = 11,
    SayGrp = 12,
    SayAlly = 13,
    PM = 15
}

/// <summary>
/// Everything but clan, group, PM and their say* versions 
/// </summary>
public enum PublicChatType
{
    // Whisper = 0,
    Whisper = 1,
    Normal = 2,
    Trade = 3,
    GM = 5,
    GM_Outgoing = 999
}