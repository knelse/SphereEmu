namespace SphServer.Helpers.Networking;

/// <summary>
///     Events PacketLogViewer already named, mapped to MBC catalog ids.
///     Catalog names are used in the list until an override is added here.
/// </summary>
public static class MbcKnownEvents
{
    public const string KeepOurs = "ours";
    public const string UseCatalog = "catalog";

    /// <summary>
    ///     event_id → which name to show. Missing keys use the catalog.
    ///     Change a value to <see cref="KeepOurs" /> to keep the PacketLogViewer name.
    /// </summary>
    public static readonly Dictionary<string, string> Choice = new()
    {
        ["C2S:2:1:*"] = KeepOurs,
        ["C2S:2:12:1"] = KeepOurs,
        ["C2S:2:12:2"] = KeepOurs,
        ["C2S:2:12:5"] = KeepOurs,
        ["C2S:2:12:8"] = KeepOurs,
        ["C2S:2:12:17"] = KeepOurs,
        ["C2S:2:4:7"] = KeepOurs,
        ["S2C:2:1:*"] = KeepOurs,
        ["S2C:210:1:*"] = KeepOurs,
        ["S2C:0:*:EKill"] = KeepOurs,
        ["C2S:2:12:10"] = KeepOurs,
    };

    public static readonly IReadOnlyDictionary<string, KnownOverlap> Overlaps = new Dictionary<string, KnownOverlap>
    {
        ["C2S:2:1:*"] = new("client.position_keepalive", "_player.Resstop",
            "0x26 / has_position. Catalog aliases TransformUpdate. Ours is the ping path."),
        ["C2S:2:12:1"] = new("client.group.action", "_player.Manager.cmd1",
            "GameplayAction.GroupActionOrPickup. Catalog name is generic."),
        ["C2S:2:12:2"] = new("client.chat.send", "_player.sendChatMsgByParts",
            "Catalog is the send-site name. Ours is the assembled chat event."),
        ["C2S:2:12:5"] = new("client.combat.damage_and_send_animation", "_player.SendAnim",
            "Swing plus the SendAnim the catalog recovered from the same command."),
        ["C2S:2:12:8"] = new("client.trade.buy", "_player.BuyIt",
            "Same event. Catalog is the MBC send-site."),
        ["C2S:2:12:17"] = new("client.stats.update.request", "_player.Manager.cmd17",
            "Ours is more detailed. Catalog never named the command."),
        ["C2S:2:4:7"] = new("client.item.use", "_player.use_send_cuse_to_server",
            "ContMan use. Catalog is the send-site."),
        ["C2S:2:12:10"] = new("client.item.take_mainhand", "_player.enableFistPwr",
            "Weak overlap. Take-mainhand also hits other ContMan/Manager shapes."),
        ["S2C:2:1:*"] = new("server.entity.position", "_player.TransformUpdate",
            "What EntityMoveParser was matching as server_move_entity."),
        ["S2C:210:1:*"] = new("server.entity.position", "monster.TransformUpdate",
            "Same TransformUpdate layout as _player region 1, with 0x3F process switches."),
        ["S2C:0:*:EKill"] = new("server.entity.despawn", "MBC.EKill",
            "EKill is process destruction. Not always a world despawn packet."),
    };

    public static string DisplayName(MbcDecodedEvent decoded)
    {
        if (Choice.TryGetValue(decoded.EventId, out var choice) && choice == KeepOurs
            && Overlaps.TryGetValue(decoded.EventId, out var overlap))
        {
            return string.IsNullOrEmpty(decoded.Summary)
                ? overlap.Ours
                : $"{overlap.Ours} {decoded.Summary}";
        }

        return decoded.EventName;
    }

    public static string? OursName(string eventId) =>
        Overlaps.TryGetValue(eventId, out var overlap) ? overlap.Ours : null;

    public static double ConfidenceScore(string confidence) =>
        confidence switch
        {
            "high" => 1.0,
            "medium" => 0.7,
            "low" => 0.4,
            "fallback" => 0.3,
            _ => 0.5
        };

    public readonly record struct KnownOverlap(string Ours, string Catalog, string Note);
}
