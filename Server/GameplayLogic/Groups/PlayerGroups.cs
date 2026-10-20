using System;
using System.Collections.Generic;
using System.Linq;
using SphServer.Client;
using SphServer.Shared.BitStream;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.WorldState;

namespace SphServer.Server.GameplayLogic.Groups;

/// <summary>
/// Groups are kept in memory only. Each member gets his own mgrSetGroup frames (Manager cmd 25), and
/// the client keeps the list by name
/// </summary>
public static class PlayerGroups
{
    // mgrSetGroup subcodes
    private const byte NewList = 5;
    private const byte AddRow = 6;
    private const byte UpdateRow = 8;
    private const byte GroupCreated = 9;
    private const byte RemoveRow = 10;
    private const byte DestroyList = 11;
    private const byte SetRow = 12;
    private const byte Disbanded = 13;

    // Group state as getLeader and the window icons read it
    private const uint Candidate = 0;
    private const uint Member = 1;
    private const uint Leader = 2;
    private const uint Online = 1;

    /// <summary>
    /// The group window has 10 rows, and candidates take rows too
    /// </summary>
    private const int MaxRows = 10;

    /// <summary>
    /// rgb(95, 165, 100), as in the original's group reply and the client's own group texts
    /// </summary>
    private const uint TextColor = 0xFF5FA564;

    // _sys texts
    private const ushort TextKicked = 174;
    private const ushort TextAccepted = 175;
    private const ushort TextLeft = 176;
    private const ushort TextBecameLeader = 177;
    private const ushort TextAsksToJoin = 179;
    private const ushort TextDisbanded = 216;
    private const ushort TextNotInGroup = 991;
    private const ushort TextInviteAccepted = 10329;
    private const ushort TextInviteDeclined = 10330;
    private const ushort TextAlreadyInGroup = 10332;
    private const ushort TextGroupFull = 10335;

    private sealed class Row (SphereClient client, string name, uint state)
    {
        public SphereClient Client { get; } = client;
        public string Name { get; } = name;
        public uint State { get; set; } = state;
    }

    /// <summary>
    /// Leader first, then members and candidates in the order they came
    /// </summary>
    private sealed class Group
    {
        public List<Row> Rows { get; } = [];
        public IEnumerable<Row> Members => Rows.Where (r => r.State != Candidate);
        public IEnumerable<Row> Candidates => Rows.Where (r => r.State == Candidate);
    }

    private static readonly List<Group> groups = [];

    /// <summary>
    /// Invited player to the leader who invited him; the latest invite wins
    /// </summary>
    private static readonly Dictionary<SphereClient, SphereClient> invites = [];

    private static readonly object sync = new ();

    public static void Create (SphereClient client)
    {
        lock (sync)
        {
            var name = NameOf (client);
            var (group, row) = Find (client);
            if (name is null || row is { State: not Candidate })
            {
                return;
            }

            if (row is not null)
            {
                RemoveCandidate (group!, row);
            }

            var created = new Group ();
            created.Rows.Add (new Row (client, name, Leader));
            groups.Add (created);
            Send (client, id => CommonPackets.BuildPlayerGroupCommand (id, NewList));
            Send (client, id => CommonPackets.BuildPlayerGroupCommand (id, GroupCreated));
            SphLogger.Info ($"Group: {name} created a group");
        }
    }

    /// <summary>
    /// Subcode 1: the player clicked someone with the join cursor, so the name is that player's
    /// </summary>
    public static void AskToJoin (SphereClient client, string targetName)
    {
        lock (sync)
        {
            var name = NameOf (client);
            var target = FindByName (targetName);
            if (name is null || target is null)
            {
                return;
            }

            var (group, targetRow) = Find (target);
            if (group is null || targetRow!.State == Candidate)
            {
                Send (client, id => CommonPackets.BuildPlayerSystemChatText (id, TextNotInGroup, TextColor));
                return;
            }

            var (ownGroup, ownRow) = Find (client);
            if (ownRow is { State: not Candidate } || ownGroup == group)
            {
                return;
            }

            if (group.Rows.Count >= MaxRows)
            {
                Send (client, id => CommonPackets.BuildPlayerSystemChatText (id, TextGroupFull, TextColor));
                return;
            }

            if (ownRow is not null)
            {
                RemoveCandidate (ownGroup!, ownRow);
            }

            group.Rows.Add (new Row (client, name, Candidate));
            var leader = group.Rows[0].Client;
            Send (leader, id => CommonPackets.BuildPlayerGroupRow (id, SetRow, name, Candidate));
            Send (leader, id => CommonPackets.BuildPlayerSystemChatWithName (id, TextAsksToJoin, TextColor, name));
            SphLogger.Info ($"Group: {name} asks to join {group.Rows[0].Name}");
        }
    }

    public static void Accept (SphereClient leader, string candidateName)
    {
        lock (sync)
        {
            var group = GroupLedBy (leader);
            var candidate = group?.Candidates.FirstOrDefault (r => NameEquals (r.Name, candidateName));
            if (candidate is not null)
            {
                Join (group!, candidate);
            }
        }
    }

    public static void Kick (SphereClient leader, string memberName)
    {
        lock (sync)
        {
            var group = GroupLedBy (leader);
            var row = group?.Rows.Skip (1).FirstOrDefault (r => NameEquals (r.Name, memberName));
            if (row is null)
            {
                return;
            }

            if (row.State == Candidate)
            {
                RemoveCandidate (group!, row);
                return;
            }

            group!.Rows.Remove (row);
            Send (row.Client, id => CommonPackets.BuildPlayerGroupCommand (id, DestroyList));
            Send (row.Client, id => CommonPackets.BuildPlayerSystemChatText (id, TextKicked, TextColor));
            foreach (var member in group.Members)
            {
                Send (member.Client, id => CommonPackets.BuildPlayerGroupRow (id, RemoveRow, row.Name));
            }

            SphLogger.Info ($"Group: {row.Name} was kicked by {group.Rows[0].Name}");
        }
    }

    public static void Disband (SphereClient leader)
    {
        lock (sync)
        {
            var group = GroupLedBy (leader);
            if (group is null)
            {
                return;
            }

            groups.Remove (group);
            foreach (var member in group.Members)
            {
                Send (member.Client, id => CommonPackets.BuildPlayerGroupCommand (id, Disbanded));
                Send (member.Client, id => CommonPackets.BuildPlayerSystemChatText (id, TextDisbanded, TextColor));
            }

            SphLogger.Info ($"Group: {group.Rows[0].Name} disbanded the group");
        }
    }

    public static void Leave (SphereClient client)
    {
        lock (sync)
        {
            Leave (client, notifySelf: true);
        }
    }

    /// <summary>
    /// Command 46: only the leader has the invite item in the player menu
    /// </summary>
    public static void Invite (SphereClient leader, string targetName)
    {
        lock (sync)
        {
            var group = GroupLedBy (leader);
            var target = FindByName (targetName);
            if (group is null || target is null || target == leader)
            {
                return;
            }

            var targetRow = Find (target).Row;
            if (targetRow is { State: not Candidate })
            {
                Send (leader, id =>
                    CommonPackets.BuildPlayerSystemChatWithName (id, TextAlreadyInGroup, TextColor, targetRow.Name));
                return;
            }

            // The key comes back with the answer; the leader's id is enough to match it
            invites[target] = leader;
            Send (target, id => CommonPackets.BuildPlayerGroupInvite (id, IdOf (leader), group.Rows[0].Name));
            SphLogger.Info ($"Group: {group.Rows[0].Name} invites {NameOf (target)}");
        }
    }

    /// <summary>
    /// Command 50 accepts, 51 declines; both carry the key from the invite
    /// </summary>
    public static void AnswerInvite (SphereClient client, uint key, bool accepted)
    {
        lock (sync)
        {
            var name = NameOf (client);
            if (name is null || !invites.TryGetValue (client, out var leader) || IdOf (leader) != key)
            {
                return;
            }

            invites.Remove (client);
            var group = GroupLedBy (leader);
            if (group is null)
            {
                return;
            }

            if (!accepted)
            {
                Send (leader, id => CommonPackets.BuildPlayerSystemChatWithName (id, TextInviteDeclined, TextColor, name));
                return;
            }

            var (ownGroup, ownRow) = Find (client);
            if (ownRow is { State: not Candidate })
            {
                return;
            }

            if (ownGroup != group)
            {
                if (group.Rows.Count >= MaxRows)
                {
                    Send (client, id => CommonPackets.BuildPlayerSystemChatText (id, TextGroupFull, TextColor));
                    return;
                }

                if (ownRow is not null)
                {
                    RemoveCandidate (ownGroup!, ownRow);
                }

                ownRow = new Row (client, name, Candidate);
                group.Rows.Add (ownRow);
            }

            Send (leader, id => CommonPackets.BuildPlayerSystemChatWithName (id, TextInviteAccepted, TextColor, name));
            Join (group, ownRow!);
        }
    }

    /// <summary>
    /// A player who leaves the game leaves his group; a leader hands it to the next member
    /// </summary>
    public static void OnClientLeftWorld (SphereClient client)
    {
        lock (sync)
        {
            Leave (client, notifySelf: false);
            invites.Remove (client);
            foreach (var invited in invites.Where (i => i.Value == client).Select (i => i.Key).ToList ())
            {
                invites.Remove (invited);
            }
        }
    }

    /// <summary>
    /// Everyone in the player's group, the player too; empty when he is not a member
    /// </summary>
    public static List<SphereClient> GetMembers (SphereClient client)
    {
        lock (sync)
        {
            var (group, row) = Find (client);
            return row is null || row.State == Candidate
                ? []
                : group!.Members.Select (r => r.Client).ToList ();
        }
    }

    /// <summary>
    /// The new member gets the list (5, a 6 row per member, 12 on himself), the others a 12 row for him
    /// </summary>
    private static void Join (Group group, Row row)
    {
        row.State = Member;
        Send (row.Client, id => CommonPackets.BuildPlayerGroupCommand (id, NewList));
        foreach (var member in group.Members.ToList ())
        {
            Send (row.Client, id => CommonPackets.BuildPlayerGroupRow (id, AddRow, member.Name, member.State, Online));
        }

        foreach (var member in group.Members)
        {
            Send (member.Client, id => CommonPackets.BuildPlayerGroupRow (id, SetRow, row.Name, Member));
        }

        Send (row.Client, id => CommonPackets.BuildPlayerSystemChatText (id, TextAccepted, TextColor));
        SphLogger.Info ($"Group: {row.Name} joined {group.Rows[0].Name}");
    }

    private static void Leave (SphereClient client, bool notifySelf)
    {
        var (group, row) = Find (client);
        if (row is null)
        {
            return;
        }

        if (row.State == Candidate)
        {
            RemoveCandidate (group!, row);
            return;
        }

        group!.Rows.Remove (row);
        if (notifySelf)
        {
            Send (client, id => CommonPackets.BuildPlayerGroupCommand (id, DestroyList));
            Send (client, id => CommonPackets.BuildPlayerSystemChatText (id, TextLeft, TextColor));
        }

        SphLogger.Info ($"Group: {row.Name} left the group");
        var members = group.Members.ToList ();
        if (members.Count == 0)
        {
            groups.Remove (group);
            return;
        }

        foreach (var member in members)
        {
            Send (member.Client, id => CommonPackets.BuildPlayerGroupRow (id, RemoveRow, row.Name));
        }

        if (row.State != Leader)
        {
            return;
        }

        // The client has no command to pass the lead, so the next member takes it
        var next = members[0];
        next.State = Leader;
        group.Rows.Remove (next);
        group.Rows.Insert (0, next);
        foreach (var member in members)
        {
            Send (member.Client, id => CommonPackets.BuildPlayerGroupRow (id, UpdateRow, next.Name, Leader, Online));
        }

        Send (next.Client, id => CommonPackets.BuildPlayerSystemChatText (id, TextBecameLeader, TextColor));
        foreach (var candidate in group.Candidates)
        {
            Send (next.Client, id => CommonPackets.BuildPlayerGroupRow (id, SetRow, candidate.Name, Candidate));
        }

        SphLogger.Info ($"Group: {next.Name} leads the group now");
    }

    /// <summary>
    /// Only the leader's window shows candidates
    /// </summary>
    private static void RemoveCandidate (Group group, Row row)
    {
        group.Rows.Remove (row);
        Send (group.Rows[0].Client, id => CommonPackets.BuildPlayerGroupRow (id, RemoveRow, row.Name));
    }

    private static Group? GroupLedBy (SphereClient leader)
    {
        var (group, row) = Find (leader);
        return row?.State == Leader ? group : null;
    }

    private static (Group? Group, Row? Row) Find (SphereClient client)
    {
        foreach (var group in groups)
        {
            var row = group.Rows.FirstOrDefault (r => r.Client == client);
            if (row is not null)
            {
                return (group, row);
            }
        }

        return (null, null);
    }

    private static SphereClient? FindByName (string name) =>
        ActiveClients.GetAll ().Values.FirstOrDefault (c =>
            c.ClientStateManager.IsInGameState () && c.CurrentCharacter is { } character &&
            NameEquals (character.Name, name));

    private static bool NameEquals (string a, string b) => string.Equals (a, b, StringComparison.OrdinalIgnoreCase);

    private static string? NameOf (SphereClient client) => client.CurrentCharacter?.Name;

    /// <summary>
    /// Same process id as the client's own records
    /// </summary>
    private static ushort IdOf (SphereClient client) => SphBitStream.ByteSwap (client.CurrentCharacter!.ClientIndex);

    private static void Send (SphereClient client, Func<ushort, byte[]> build)
    {
        if (client.CurrentCharacter is null)
        {
            return;
        }

        client.MaybeQueueNetworkPacketSend (build (IdOf (client)));
    }
}
