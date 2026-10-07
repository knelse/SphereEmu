using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BitStreams;
using Godot;
using SphereHelpers.Extensions;
using SphServer.Helpers.Networking;
using SphServer.Shared.BitStream;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Client.Networking.Handlers.InGame.DamageHealEffects;

/// <summary>
/// Classification of an incoming combat frame. Length is not part of the identity.
/// </summary>
public enum AttackFrameKind
{
    /// <summary>
    /// Left-click / use-on-target: GameplayAction.Attack, usually seen as 08 40 A3 at bytes 13-15.
    /// </summary>
    MeleeOrMagicAttack = 0,

    /// <summary>
    /// Alt-self (54 43 C1, own id at bits 172-187) is a real hit, and Radius still splashes
    /// </summary>
    SelfTargetedAction = 1,

    /// <summary>
    /// 7E 14 CE at bytes 22-24; the target id is not in the fist slot
    /// </summary>
    WeaponAttack = 2,

    /// <summary>
    /// Not an attack, e.g. 08 40 83 right-click or use on a dead target, absorbed silently
    /// </summary>
    NotAnAttack = 3
}

// 0x19/0x20 fallback: anything that is not a buy (08 40 03) lands here
public class DamageTargetHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (byte[] frame, double delta)
    {
        // Ack first so g_6008 clears even when parse returns early
        clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.ClearUseToutAck (localId));

        var character = clientConnection.GetSelectedCharacter ();
        var broadcastTarget = ActiveClients.Get (localId);
        if (character is null || broadcastTarget is null)
        {
            // Source is the raw local id: with no client there is nothing to resolve it against.
            LogAction (localId, 0, AttackFrameKind.NotAnAttack,
                character is null ? "skip-no-character" : "skip-no-client");
            return;
        }

        SphLogger.Info ($"DamageTargetHandler: {Convert.ToHexString (frame)}");

        var broadcastTargetGlobalId = broadcastTarget.GetGlobalObjectId (localId);
        var heldItem = character.GetHeldItem ();
        var frameKind = ParseAttackFrame (frame, heldItem, out var localTargetId);

        // 0x19 empty-to-sword shares 08 40 A3 with a fist swing, but the item id is the take
        if (IsTakeMainhand (character, frame, localTargetId))
        {
            LogAction (broadcastTargetGlobalId, localTargetId, frameKind, "take-mainhand");
            await clientConnection.HandleTakeMainhand (frame, delta);
            return;
        }

        if (frameKind is AttackFrameKind.NotAnAttack)
        {
            LogAction (broadcastTargetGlobalId, localTargetId, frameKind, "skip");
            return;
        }

        // 0 = no id in the frame, 0xFFFF = the client's no-target sentinel
        // (target despawned mid-click).
        if (localTargetId is 0 or ushort.MaxValue)
        {
            LogAction (broadcastTargetGlobalId, localTargetId, frameKind, "skip");
            return;
        }

        var globalTargetId = broadcastTarget.GetGlobalObjectId (localTargetId);
        // Fists have no Radius, so they never splash
        var aoeRadius = heldItem.GameObjectType is GameObjectType.Fists ? 0 : heldItem.Radius;
        // Self-aimed wire is often ByteSwap(clientId), and a blast on a mob still includes the
        // caster inside Radius
        var includeSelf = frameKind is AttackFrameKind.SelfTargetedAction
                          || IsAttackerTargetId (broadcastTarget, globalTargetId);
        var targets = CollectTargets (broadcastTarget, globalTargetId, aoeRadius, includeSelf).ToList ();
        LogAction (broadcastTargetGlobalId, globalTargetId, frameKind,
            $"held={heldItem.GameId} radius={aoeRadius} includeSelf={includeSelf} targets={targets.Count}");
        foreach (var (targetGlobalId, targetLocalId) in targets)
        {
            clientConnection.EnqueueClientEvent (new CombatHitEvent (
                broadcastTargetGlobalId, targetGlobalId, targetLocalId, frameKind));
        }
    }

    /// <summary>
    /// Live client id, WorldObject.ID, or the byte-swapped client id on self-aimed AoE
    /// </summary>
    private static bool IsAttackerTargetId (SphereClient attacker, ushort globalId)
    {
        var clientId = attacker.GetGlobalObjectId (attacker.localId);
        return globalId == clientId
               || globalId == attacker.ID
               || globalId == SphBitStream.ByteSwap (clientId);
    }

    private static bool IsTakeMainhand (CharacterDbEntry character, byte[] frame, ushort combatTargetId)
    {
        if (ClientPacketClassifier.IsHandChangeSignature (frame))
        {
            return true;
        }

        var declaredLength = frame[0] | (frame[1] << 8);
        if (MainhandFrame.IsExclusiveTakeLength (declaredLength))
        {
            return true;
        }

        if (MainhandFrame.TryRead (frame, out var take))
        {
            // 0x19 A3 + 0xFFFF is put-down, A3 plus an owned item is a take, and a monster id there
            // is a swing
            return take.ItemId == MainhandFrame.NoItem || IsOwnedItemId (character, take.ItemId);
        }

        return IsOwnedItemId (character, combatTargetId);
    }

    private static bool IsOwnedItemId (CharacterDbEntry character, ushort id)
    {
        if (id is 0 or ushort.MaxValue)
        {
            return false;
        }

        if (character.Items.ContainsValue (id))
        {
            return true;
        }

        if (ActiveWorldObjects.Get (id) is Monster or SphereClient)
        {
            return false;
        }

        // Hotkey take after the item left MainHand: still a catalog row, never a live combatant.
        return DbConnection.Items.FindById ((int) id) is not null;
    }

    private static void LogAction (ushort sourceGlobalId, ushort targetGlobalId, AttackFrameKind action, string result)
    {
        SphLogger.Info ($"DamageTargetHandler: Source [{sourceGlobalId:X4}] - Target [{targetGlobalId:X4}] - " +
                       $"Action [{action}] - [{result}]");
    }

    /// <summary>
    /// Fist / bare-hand layout: the clicked id sits here.
    /// </summary>
    private const int FistTargetBit = 172;

    /// <summary>
    /// AoE powder list: first (aimed) target. Single-target powder is not this slot.
    /// </summary>
    private const int AoeTargetBit = 220;

    /// <summary>
    /// Weapon swing slot; bit 172 on these frames is the 12-bit ObjectType, not a target
    /// </summary>
    private const int ArmedTargetBit = 233;

    /// <summary>
    /// 12-bit ObjectType sitting next to the fist-target slot on item-use frames.
    /// </summary>
    private const int ItemObjectTypeBit = 174;

    /// <summary>
    /// Length is not identity; splash ids in the frame are ignored and AoE comes from the held item
    /// </summary>
    public static AttackFrameKind ParseAttackFrame (byte[] receiveBuffer, out ushort localTargetId) =>
        ParseAttackFrame (receiveBuffer, null, out localTargetId);

    public static AttackFrameKind ParseAttackFrame (byte[] receiveBuffer, ItemDbEntry? heldItem,
        out ushort localTargetId)
    {
        localTargetId = 0;
        if (receiveBuffer.Length < 16)
        {
            return AttackFrameKind.NotAnAttack;
        }

        if (ClientPacketClassifier.IsHandChangeSignature (receiveBuffer))
        {
            return AttackFrameKind.NotAnAttack;
        }

        if (receiveBuffer[13] == 0x54 && receiveBuffer[14] == 0x43 && receiveBuffer[15] == 0xC1)
        {
            localTargetId = ReadU16AtBit (receiveBuffer, FistTargetBit);
            return AttackFrameKind.SelfTargetedAction;
        }

        if (receiveBuffer.Length >= 25 && receiveBuffer[22] == 0x7E && receiveBuffer[23] == 0x14 &&
            receiveBuffer[24] == 0xCE)
        {
            localTargetId = ReadMainTargetId (receiveBuffer, heldItem);
            return AttackFrameKind.WeaponAttack;
        }

        // 08 40 A3 is shared with take, swap, and NPC interact; GameplayAction.Attack is the
        // length-independent check
        var attackSignature = receiveBuffer[13] == 0x08 && receiveBuffer[14] == 0x40 && receiveBuffer[15] == 0xA3;
        var attackRecord = GameplayRecord.ActionOf (GameplayRecord.ReadIdentity (receiveBuffer)) ==
                           GameplayAction.Attack;
        if (!attackSignature && !attackRecord)
        {
            return AttackFrameKind.NotAnAttack;
        }

        localTargetId = ReadMainTargetId (receiveBuffer, heldItem);
        return AttackFrameKind.MeleeOrMagicAttack;
    }

    /// <summary>
    /// A 0x2C swing still has the weapon type at the item slot, so bit 172 is that type (501 as
    /// 47D4), not the mob
    /// </summary>
    private static ushort ReadMainTargetId (byte[] frame, ItemDbEntry? heldItem)
    {
        var typeAtItemSlot = ReadU12AtBit (frame, ItemObjectTypeBit);
        if (IsAoePowderLayout (heldItem, typeAtItemSlot))
        {
            return ReadU16AtBit (frame, AoeTargetBit);
        }

        if (IsArmedAttackLayout (frame))
        {
            return ReadU16AtBit (frame, ArmedTargetBit);
        }

        if (heldItem is null || heldItem.GameObjectType is GameObjectType.Fists)
        {
            return ReadU16AtBit (frame, FistTargetBit);
        }

        return ReadU16AtBit (frame, ArmedTargetBit);
    }

    /// <summary>
    /// 0x2C is the armed swing; on 0x19 the type slot is two bits into the take id
    /// </summary>
    private static bool IsArmedAttackLayout (byte[] frame)
    {
        var declaredLength = frame.Length >= 2 ? frame[0] | (frame[1] << 8) : 0;
        return declaredLength == 0x2C;
    }

    /// <summary>
    /// AoE powder (0x2D/0x30) aims at bit 220; single-target powder is a 0x2C item-use
    /// </summary>
    private static bool IsAoePowderLayout (ItemDbEntry? heldItem, ushort typeAtItemSlot) =>
        typeAtItemSlot == (ushort) ObjectType.Powder_Ao_E ||
        heldItem is { Radius: > 0, GameObjectType: GameObjectType.Powder_Area };

    private static IEnumerable<(ushort GlobalId, ushort LocalId)> CollectTargets (SphereClient attacker,
        ushort mainGlobalId, int aoeRadius, bool includeSelf)
    {
        var mainIsSelf = IsAttackerTargetId (attacker, mainGlobalId);
        if (includeSelf || !mainIsSelf)
        {
            // Self-aimed frames may carry ByteSwap(clientId), so this yields the live client id
            var yieldGlobalId = mainIsSelf
                ? attacker.GetGlobalObjectId (attacker.localId)
                : mainGlobalId;
            yield return (yieldGlobalId, attacker.GetLocalObjectId (yieldGlobalId));
        }

        if (aoeRadius <= 0)
        {
            yield break;
        }

        if (!TryResolveAoeCenter (attacker, mainGlobalId, out var center))
        {
            yield break;
        }

        var radiusSquared = aoeRadius * (float) aoeRadius;
        // The aim point is the mob, so the caster is a splash target inside that disk
        if (!mainIsSelf && TryGetCombatWorldPosition (attacker, out var selfPosition) &&
            center.DistanceSquaredTo (selfPosition) <= radiusSquared)
        {
            var selfId = attacker.GetGlobalObjectId (attacker.localId);
            yield return (selfId, attacker.GetLocalObjectId (selfId));
        }
        var clientId = attacker.GetGlobalObjectId (attacker.localId);
        var seen = new HashSet<ushort>
        {
            mainGlobalId,
            clientId,
            attacker.ID,
            SphBitStream.ByteSwap (clientId)
        };

        foreach (var worldObject in ActiveWorldObjects.GetAll ().Values)
        {
            if (worldObject is not Monster and not SphereClient)
            {
                continue;
            }

            if (!GodotObject.IsInstanceValid (worldObject) || !seen.Add (worldObject.ID))
            {
                continue;
            }

            if (IsAttackerTargetId (attacker, worldObject.ID))
            {
                continue;
            }

            if (!TryGetCombatWorldPosition (worldObject, out var position))
            {
                continue;
            }

            if (center.DistanceSquaredTo (position) > radiusSquared)
            {
                continue;
            }

            if (worldObject is Monster { IsDead: true })
            {
                continue;
            }

            yield return (worldObject.ID, attacker.GetLocalObjectId (worldObject.ID));
        }

        foreach (var otherClient in ActiveClients.GetAll ().Values)
        {
            if (otherClient is null || IsAttackerTargetId (attacker, otherClient.localId) ||
                IsAttackerTargetId (attacker, otherClient.ID) || !seen.Add (otherClient.ID))
            {
                continue;
            }

            if (!ClientWorldPosition.TryGetGodotWorldPosition (otherClient, out var position))
            {
                continue;
            }

            if (center.DistanceSquaredTo (position) > radiusSquared)
            {
                continue;
            }

            yield return (otherClient.ID, attacker.GetLocalObjectId (otherClient.ID));
        }
    }

    /// <summary>
    /// Aimed object, or the attacker when the aim is self; a missing non-self aim aborts splash
    /// </summary>
    private static bool TryResolveAoeCenter (SphereClient attacker, ushort mainGlobalId, out Vector3 center)
    {
        if (IsAttackerTargetId (attacker, mainGlobalId))
        {
            return TryGetCombatWorldPosition (attacker, out center);
        }

        var mainObject = ActiveWorldObjects.Get (mainGlobalId);
        if (mainObject is null || !GodotObject.IsInstanceValid (mainObject))
        {
            center = Vector3.Zero;
            return false;
        }

        return TryGetCombatWorldPosition (mainObject, out center);
    }

    private static bool TryGetCombatWorldPosition (WorldObject worldObject, out Vector3 position)
    {
        if (worldObject is SphereClient client)
        {
            return ClientWorldPosition.TryGetGodotWorldPosition (client, out position);
        }

        if (!GodotObject.IsInstanceValid (worldObject))
        {
            position = Vector3.Zero;
            return false;
        }

        position = worldObject.GlobalPosition;
        return true;
    }

    private static ushort ReadU16AtBit (byte[] frame, int bit)
    {
        if (frame.Length * 8 < bit + 16)
        {
            return 0;
        }

        var stream = new BitStream (frame);
        stream.ReadBits (bit);
        return stream.ReadUInt16 ();
    }

    private static ushort ReadU12AtBit (byte[] frame, int bit)
    {
        if (frame.Length * 8 < bit + 12)
        {
            return 0;
        }

        var stream = new BitStream (frame);
        stream.ReadBits (bit);
        return stream.ReadUInt16 (12);
    }
}
