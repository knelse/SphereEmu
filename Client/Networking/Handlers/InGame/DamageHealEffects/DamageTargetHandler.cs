using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BitStreams;
using Godot;
using SphereHelpers.Extensions;
using SphServer.Helpers.Networking;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Client.Networking.Handlers.InGame.DamageHealEffects;

/// <summary>Classification of an incoming combat frame. Length is not part of the identity.</summary>
public enum AttackFrameKind
{
    /// <summary>Left-click / use-on-target: GameplayAction.Attack, usually seen as 08 40 A3 at bytes 13-15.</summary>
    MeleeOrMagicAttack = 0,

    /// <summary>
    ///     Self-targeted action (Alt modifier: target = the player themselves, hence the player's own
    ///     id at bits 172-187; 54 43 C1 at bytes 13-15). Meant for self-casts like heal mantras;
    ///     a self-targeted fist attack is meaningless, so it is dropped in v1.
    /// </summary>
    SelfTargetedAction = 1,

    /// <summary>
    ///     Weapon swing (7E 14 CE at bytes 22-24): weapons attack via the item-use path, so the frame
    ///     shape differs from <see cref="MeleeOrMagicAttack" /> and its target id sits elsewhere.
    /// </summary>
    WeaponAttack = 2,

    /// <summary>Not an attack, e.g. 08 40 83 = right-click interact / use on a dead target — absorbed silently.</summary>
    NotAnAttack = 3
}

// Fallback branch of first bytes 0x19/0x20: every frame that is not a buy-item request
// (08 40 03, routed to BuyItemFromTargetHandler first) lands here.
public class DamageTargetHandler(ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public async Task Handle(byte[] frame, double delta)
    {
        // Attack-wedge fix (#11): ack the use first — before any parse or early return — so the
        // client's use-lock (g_6008) is always cleared. See CommonPackets.ClearUseToutAck.
        clientConnection.MaybeScheduleNetworkPacketSend(CommonPackets.ClearUseToutAck(localId));

        var character = clientConnection.GetSelectedCharacter();
        var broadcastTarget = ActiveClients.Get(localId);
        if (character is null || broadcastTarget is null)
        {
            // Source is the raw local id: with no client there is nothing to resolve it against.
            LogAction(localId, 0, AttackFrameKind.NotAnAttack,
                character is null ? "skip-no-character" : "skip-no-client");
            return;
        }

        SphLogger.Info($"DamageTargetHandler: {Convert.ToHexString(frame)}");

        var broadcastTargetGlobalId = broadcastTarget.GetGlobalObjectId(localId);
        var heldItem = character.GetHeldItem();
        var frameKind = ParseAttackFrame(frame, heldItem, out var localTargetId);

        // 0x19 empty→sword shares Attack + 08 40 A3 with a fist swing. The ksy item id is the
        // take; the combat target field is the wrong slot once the frame is an armed swing.
        if (IsTakeMainhand(character, frame, localTargetId))
        {
            LogAction(broadcastTargetGlobalId, localTargetId, frameKind, "take-mainhand");
            await clientConnection.HandleTakeMainhand(frame, delta);
            return;
        }

        if (frameKind is AttackFrameKind.SelfTargetedAction or AttackFrameKind.NotAnAttack)
        {
            LogAction(broadcastTargetGlobalId, localTargetId, frameKind, "skip");
            return;
        }

        // 0 = no id in the frame, 0xFFFF = the client's no-target sentinel (target despawned mid-click).
        if (localTargetId is 0 or ushort.MaxValue)
        {
            LogAction(broadcastTargetGlobalId, localTargetId, frameKind, "skip");
            return;
        }

        var globalTargetId = broadcastTarget.GetGlobalObjectId(localTargetId);
        // Fists never splash. Only a real held item can carry a Radius.
        var aoeRadius = heldItem.GameObjectType is GameObjectType.Fists ? 0 : heldItem.Radius;
        var targets = CollectTargets(broadcastTarget, globalTargetId, aoeRadius).ToList();
        LogAction(broadcastTargetGlobalId, globalTargetId, frameKind,
            $"held={heldItem.GameId} radius={aoeRadius} targets={targets.Count}");
        foreach (var (targetGlobalId, targetLocalId) in targets)
        {
            clientConnection.EnqueueClientEvent(new CombatHitEvent(
                broadcastTargetGlobalId, targetGlobalId, targetLocalId, frameKind));
        }
    }

    private static bool IsTakeMainhand(CharacterDbEntry character, byte[] frame, ushort combatTargetId)
    {
        if (ClientPacketClassifier.IsHandChangeSignature(frame))
        {
            return true;
        }

        var declaredLength = frame[0] | (frame[1] << 8);
        if (MainhandFrame.IsExclusiveTakeLength(declaredLength))
        {
            return true;
        }

        if (MainhandFrame.TryRead(frame, out var take))
        {
            // 0x19: fists unequip is 08 40 83 (already classified). A3 + 0xFFFF is put-down;
            // A3 + an owned item is empty/fists → sword. A monster id at that slot is a swing.
            return take.ItemId == MainhandFrame.NoItem || IsOwnedItemId(character, take.ItemId);
        }

        return IsOwnedItemId(character, combatTargetId);
    }

    private static bool IsOwnedItemId(CharacterDbEntry character, ushort id)
    {
        if (id is 0 or ushort.MaxValue)
        {
            return false;
        }

        if (character.Items.ContainsValue(id))
        {
            return true;
        }

        if (ActiveWorldObjects.Get(id) is Monster or SphereClient)
        {
            return false;
        }

        // Hotkey take after the item left MainHand: still a catalog row, never a live combatant.
        return DbConnection.Items.FindById((int)id) is not null;
    }

    private static void LogAction(ushort sourceGlobalId, ushort targetGlobalId, AttackFrameKind action, string result)
    {
        SphLogger.Info($"DamageTargetHandler: Source [{sourceGlobalId:X4}] - Target [{targetGlobalId:X4}] - " +
                       $"Action [{action}] - [{result}]");
    }

    /// <summary>Fist / bare-hand layout: the clicked id sits here.</summary>
    private const int FistTargetBit = 172;

    /// <summary>AoE powder list: first (aimed) target. Single-target powder is not this slot.</summary>
    private const int AoeTargetBit = 220;

    /// <summary>
    ///     Default item-use / weapon swing slot. Fist-slot 172 on these frames is the 12-bit
    ///     ObjectType misread as a u16, not a target.
    /// </summary>
    private const int ArmedTargetBit = 233;

    /// <summary>12-bit ObjectType sitting next to the fist-target slot on item-use frames.</summary>
    private const int ItemObjectTypeBit = 174;

    /// <summary>
    ///     Classifies a combat frame without using its length prefix, then reads the aimed target.
    ///     Extra client-side splash ids are ignored; AoE expansion is server-side from the held item.
    /// </summary>
    public static AttackFrameKind ParseAttackFrame(byte[] receiveBuffer, out ushort localTargetId) =>
        ParseAttackFrame(receiveBuffer, null, out localTargetId);

    public static AttackFrameKind ParseAttackFrame(byte[] receiveBuffer, ItemDbEntry? heldItem,
        out ushort localTargetId)
    {
        localTargetId = 0;
        if (receiveBuffer.Length < 16)
        {
            return AttackFrameKind.NotAnAttack;
        }

        if (ClientPacketClassifier.IsHandChangeSignature(receiveBuffer))
        {
            return AttackFrameKind.NotAnAttack;
        }

        if (receiveBuffer[13] == 0x54 && receiveBuffer[14] == 0x43 && receiveBuffer[15] == 0xC1)
        {
            localTargetId = ReadU16AtBit(receiveBuffer, FistTargetBit);
            return AttackFrameKind.SelfTargetedAction;
        }

        if (receiveBuffer.Length >= 25 && receiveBuffer[22] == 0x7E && receiveBuffer[23] == 0x14 &&
            receiveBuffer[24] == 0xCE)
        {
            localTargetId = ReadMainTargetId(receiveBuffer, heldItem);
            return AttackFrameKind.WeaponAttack;
        }

        // 08 40 A3 is the player-action record as bytes (subject type 2 + tag 13), not unique to
        // attacks: take-mainhand, swap, and NPC interact share it. GameplayAction.Attack (action 5,
        // flag clear) is the length-independent confirmation. Either is enough here because this
        // handler is only reached after the classifier has already routed CombatDamageTarget.
        var attackSignature = receiveBuffer[13] == 0x08 && receiveBuffer[14] == 0x40 && receiveBuffer[15] == 0xA3;
        var attackRecord = GameplayRecord.ActionOf(GameplayRecord.ReadIdentity(receiveBuffer)) ==
                           GameplayAction.Attack;
        if (!attackSignature && !attackRecord)
        {
            return AttackFrameKind.NotAnAttack;
        }

        localTargetId = ReadMainTargetId(receiveBuffer, heldItem);
        return AttackFrameKind.MeleeOrMagicAttack;
    }

    /// <summary>
    ///     Aimed target only. Layout comes from the frame, not from MainHand: a 0x2C armed
    ///     swing still carries WeaponAxe (etc.) at the item-type slot after a fist→weapon
    ///     swap the server has not applied yet. Reading bit 172 on that shape returns the
    ///     12-bit ObjectType as a u16 (501 → 47D4), not the mob.
    /// </summary>
    private static ushort ReadMainTargetId(byte[] frame, ItemDbEntry? heldItem)
    {
        var typeAtItemSlot = ReadU12AtBit(frame, ItemObjectTypeBit);
        if (IsAoePowderLayout(heldItem, typeAtItemSlot))
        {
            return ReadU16AtBit(frame, AoeTargetBit);
        }

        if (IsArmedAttackLayout(frame))
        {
            return ReadU16AtBit(frame, ArmedTargetBit);
        }

        if (heldItem is null || heldItem.GameObjectType is GameObjectType.Fists)
        {
            return ReadU16AtBit(frame, FistTargetBit);
        }

        return ReadU16AtBit(frame, ArmedTargetBit);
    }

    /// <summary>
    ///     0x2C + 08 40 A3 is the armed swing (classifier). Do not use the 12-bit ObjectType
    ///     at <see cref="ItemObjectTypeBit"/> on 0x19: that slot is two bits into the take
    ///     item id / fist target, and a weapon-looking slice would steal the wrong u16.
    /// </summary>
    private static bool IsArmedAttackLayout(byte[] frame)
    {
        var declaredLength = frame.Length >= 2 ? frame[0] | (frame[1] << 8) : 0;
        return declaredLength == 0x2C;
    }

    /// <summary>
    ///     AoE powder frames (0x2D/0x30) put the aimed id at <see cref="AoeTargetBit"/>.
    ///     Single-target powder is a 0x2C item-use and uses <see cref="ArmedTargetBit"/> instead.
    /// </summary>
    private static bool IsAoePowderLayout(ItemDbEntry? heldItem, ushort typeAtItemSlot) =>
        typeAtItemSlot == (ushort)ObjectType.PowderAoE ||
        heldItem is { Radius: > 0, GameObjectType: GameObjectType.Powder_Area };

    private static IEnumerable<(ushort GlobalId, ushort LocalId)> CollectTargets(SphereClient attacker,
        ushort mainGlobalId, int aoeRadius)
    {
        yield return (mainGlobalId, attacker.GetLocalObjectId(mainGlobalId));

        if (aoeRadius <= 0)
        {
            yield break;
        }

        var mainObject = ActiveWorldObjects.Get(mainGlobalId);
        if (mainObject is null || !GodotObject.IsInstanceValid(mainObject) ||
            !TryGetCombatWorldPosition(mainObject, out var center))
        {
            yield break;
        }

        var radiusSquared = aoeRadius * (float)aoeRadius;
        var seen = new HashSet<ushort> { mainGlobalId, attacker.GetGlobalObjectId(attacker.localId) };

        foreach (var worldObject in ActiveWorldObjects.GetAll().Values)
        {
            if (worldObject is not Monster and not SphereClient)
            {
                continue;
            }

            if (!GodotObject.IsInstanceValid(worldObject) || !seen.Add(worldObject.ID))
            {
                continue;
            }

            if (!TryGetCombatWorldPosition(worldObject, out var position))
            {
                continue;
            }

            if (center.DistanceSquaredTo(position) > radiusSquared)
            {
                continue;
            }

            if (worldObject is Monster { IsDead: true })
            {
                continue;
            }

            yield return (worldObject.ID, attacker.GetLocalObjectId(worldObject.ID));
        }

        foreach (var otherClient in ActiveClients.GetAll().Values)
        {
            if (otherClient is null || !seen.Add(otherClient.ID))
            {
                continue;
            }

            if (!ClientWorldPosition.TryGetGodotWorldPosition(otherClient, out var position))
            {
                continue;
            }

            if (center.DistanceSquaredTo(position) > radiusSquared)
            {
                continue;
            }

            yield return (otherClient.ID, attacker.GetLocalObjectId(otherClient.ID));
        }
    }

    private static bool TryGetCombatWorldPosition(WorldObject worldObject, out Vector3 position)
    {
        if (worldObject is SphereClient client)
        {
            return ClientWorldPosition.TryGetGodotWorldPosition(client, out position);
        }

        if (!GodotObject.IsInstanceValid(worldObject))
        {
            position = Vector3.Zero;
            return false;
        }

        position = worldObject.GlobalPosition;
        return true;
    }

    private static ushort ReadU16AtBit(byte[] frame, int bit)
    {
        if (frame.Length * 8 < bit + 16)
        {
            return 0;
        }

        var stream = new BitStream(frame);
        stream.ReadBits(bit);
        return stream.ReadUInt16();
    }

    private static ushort ReadU12AtBit(byte[] frame, int bit)
    {
        if (frame.Length * 8 < bit + 12)
        {
            return 0;
        }

        var stream = new BitStream(frame);
        stream.ReadBits(bit);
        return stream.ReadUInt16(12);
    }
}
