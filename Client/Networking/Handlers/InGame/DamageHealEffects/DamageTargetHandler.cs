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
public class DamageTargetHandler (Guid entityId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (byte[] frame, double delta)
    {
        // Ack first so g_6008 clears even when parse returns early
        clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.ClearUseToutAck (entityId));

        var character = clientConnection.GetSelectedCharacter ();
        var broadcastTarget = ActiveClients.Get (entityId);
        if (character is null || broadcastTarget is null)
        {
            // Source is the raw local id: with no client there is nothing to resolve it against.
            LogAction (entityId, Guid.Empty, AttackFrameKind.NotAnAttack,
                character is null ? "skip-no-character" : "skip-no-client");
            return;
        }

        SphLogger.Info ($"DamageTargetHandler: {Convert.ToHexString (frame)}");

        var attackerEntityId = entityId;
        var heldItem = character.GetHeldItem ();
        var frameKind = ParseAttackFrame (frame, heldItem, out var localTargetId);

        // 0x19 empty-to-sword shares 08 40 A3 with a fist swing, but the item id is the take
        if (IsTakeMainhand (character, frame, localTargetId))
        {
            LogAction (attackerEntityId, localTargetId, frameKind, "take-mainhand");
            await clientConnection.HandleTakeMainhand (frame, delta);
            return;
        }

        if (frameKind is AttackFrameKind.NotAnAttack)
        {
            LogAction (attackerEntityId, localTargetId, frameKind, "skip");
            return;
        }

        // 0 = no id in the frame, 0xFFFF = the client's no-target sentinel
        // (target despawned mid-click).
        if (localTargetId == Guid.Empty)
        {
            LogAction (attackerEntityId, localTargetId, frameKind, "skip");
            return;
        }

        var targetEntityId = localTargetId;
        // Fists have no Radius, so they never splash
        var aoeRadius = heldItem.GameObjectType is GameObjectType.Fists ? 0 : heldItem.Radius;
        // Self-aimed wire is often ByteSwap(clientId), and a blast on a mob still includes the
        // caster inside Radius
        var includeSelf = frameKind is AttackFrameKind.SelfTargetedAction
                          || IsAttackerTargetId (broadcastTarget, targetEntityId);
        var targets = CollectTargets (broadcastTarget, targetEntityId, aoeRadius, includeSelf).ToList ();
        LogAction (attackerEntityId, targetEntityId, frameKind,
            $"held={heldItem.GameId} radius={aoeRadius} includeSelf={includeSelf} targets={targets.Count}");
        foreach (var hitTargetEntityId in targets)
        {
            clientConnection.EnqueueClientEvent (new CombatHitEvent (
                attackerEntityId, hitTargetEntityId, frameKind));
        }
    }

    private static bool IsAttackerTargetId (SphereClient attacker, Guid entityId)
    {
        return entityId == attacker.EntityID;
    }

    private static bool IsTakeMainhand (CharacterDbEntry character, byte[] frame, Guid combatTargetId)
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
            return take.IsUnequip || IsOwnedItemId (character, take.ItemId);
        }

        return IsOwnedItemId (character, combatTargetId);
    }

    private static bool IsOwnedItemId (CharacterDbEntry character, Guid id)
    {
        if (id == Guid.Empty)
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

        return DbConnection.Items.FindById (id) is not null;
    }

    private static void LogAction (Guid sourceEntityId, Guid targetEntityId, AttackFrameKind action, string result)
    {
        SphLogger.Info ($"DamageTargetHandler: Source [{sourceEntityId}] - Target [{targetEntityId}] - " +
                       $"Action [{action}] - [{result}]");
    }

    /// <summary>
    /// Fist / bare-hand layout: the clicked id sits here.
    /// </summary>
    private const int FistTargetBit = 282;

    /// <summary>
    /// AoE powder list: first (aimed) target. Single-target powder is not this slot.
    /// </summary>
    private const int AoeTargetBit = 440;

    /// <summary>
    /// Weapon swing slot; bit 394 on these frames is the 12-bit ObjectType, not a target
    /// </summary>
    private const int ArmedTargetBit = 453;

    /// <summary>
    /// 12-bit ObjectType sitting next to the fist-target slot on item-use frames.
    /// </summary>
    private const int ItemObjectTypeBit = 394;

    /// <summary>
    /// Length is not identity; splash ids in the frame are ignored and AoE comes from the held item
    /// </summary>
    public static AttackFrameKind ParseAttackFrame (byte[] receiveBuffer, out Guid localTargetId) =>
        ParseAttackFrame (receiveBuffer, null, out localTargetId);

    public static AttackFrameKind ParseAttackFrame (byte[] receiveBuffer, ItemDbEntry? heldItem,
        out Guid localTargetId)
    {
        localTargetId = Guid.Empty;
        if (receiveBuffer.Length < 30)
        {
            return AttackFrameKind.NotAnAttack;
        }

        if (ClientPacketClassifier.IsHandChangeSignature (receiveBuffer))
        {
            return AttackFrameKind.NotAnAttack;
        }

        var b13 = ClientPacketClassifier.ShiftedByte (receiveBuffer, 13);
        var b14 = ClientPacketClassifier.ShiftedByte (receiveBuffer, 14);
        var b15 = ClientPacketClassifier.ShiftedByte (receiveBuffer, 15);

        if (b13 == 0x54 && b14 == 0x43 && b15 == 0xC1)
        {
            localTargetId = ReadGuidAtBit (receiveBuffer, FistTargetBit);
            return AttackFrameKind.SelfTargetedAction;
        }

        if (receiveBuffer.Length >= 53
            && (byte) GameplayRecord.ReadBits (receiveBuffer, 22 * 8 + 220, 8) == 0x7E
            && (byte) GameplayRecord.ReadBits (receiveBuffer, 23 * 8 + 220, 8) == 0x14
            && (byte) GameplayRecord.ReadBits (receiveBuffer, 24 * 8 + 220, 8) == 0xCE)
        {
            localTargetId = ReadMainTargetId (receiveBuffer, heldItem);
            return AttackFrameKind.WeaponAttack;
        }

        // 08 40 A3 is shared with take, swap, and NPC interact; GameplayAction.Attack is the
        // length-independent check
        var attackSignature = b13 == 0x08 && b14 == 0x40 && b15 == 0xA3;
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
    /// A 0x62 swing still has the weapon type at the item slot, so bit 394 is that type, not the
    /// mob
    /// </summary>
    private static Guid ReadMainTargetId (byte[] frame, ItemDbEntry? heldItem)
    {
        var typeAtItemSlot = ReadU12AtBit (frame, ItemObjectTypeBit);
        if (IsAoePowderLayout (heldItem, typeAtItemSlot))
        {
            return ReadGuidAtBit (frame, AoeTargetBit);
        }

        if (IsArmedAttackLayout (frame))
        {
            return ReadGuidAtBit (frame, ArmedTargetBit);
        }

        if (heldItem is null || heldItem.GameObjectType is GameObjectType.Fists)
        {
            return ReadGuidAtBit (frame, FistTargetBit);
        }

        return ReadGuidAtBit (frame, ArmedTargetBit);
    }

    /// <summary>
    /// 0x62 is the armed swing; on 0x41 the type slot is two bits into the take id
    /// </summary>
    private static bool IsArmedAttackLayout (byte[] frame)
    {
        var declaredLength = frame.Length >= 2 ? frame[0] | (frame[1] << 8) : 0;
        return declaredLength == 0x62;
    }

    /// <summary>
    /// AoE powder (0x2D/0x30) aims at bit 220; single-target powder is a 0x2C item-use
    /// </summary>
    private static bool IsAoePowderLayout (ItemDbEntry? heldItem, ushort typeAtItemSlot) =>
        typeAtItemSlot == (ushort) ObjectType.Powder_Ao_E ||
        heldItem is { Radius: > 0, GameObjectType: GameObjectType.Powder_Area };

    private static IEnumerable<Guid> CollectTargets (SphereClient attacker,
        Guid mainTargetEntityId, int aoeRadius, bool includeSelf)
    {
        var mainIsSelf = IsAttackerTargetId (attacker, mainTargetEntityId);
        if (includeSelf || !mainIsSelf)
        {
            yield return mainIsSelf ? attacker.EntityID : mainTargetEntityId;
        }

        if (aoeRadius <= 0)
        {
            yield break;
        }

        if (!TryResolveAoeCenter (attacker, mainTargetEntityId, out var center))
        {
            yield break;
        }

        var radiusSquared = aoeRadius * (float) aoeRadius;
        // The aim point is the mob, so the caster is a splash target inside that disk
        if (!mainIsSelf && TryGetCombatWorldPosition (attacker, out var selfPosition) &&
            center.DistanceSquaredTo (selfPosition) <= radiusSquared)
        {
            yield return attacker.EntityID;
        }
        var seen = new HashSet<Guid>
        {
            mainTargetEntityId,
            attacker.EntityID
        };

        foreach (var worldObject in ActiveWorldObjects.GetAll ().Values)
        {
            if (worldObject is not Monster and not SphereClient)
            {
                continue;
            }

            if (!GodotObject.IsInstanceValid (worldObject) || !seen.Add (worldObject.EntityID))
            {
                continue;
            }

            if (IsAttackerTargetId (attacker, worldObject.EntityID))
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

            yield return worldObject.EntityID;
        }

        foreach (var otherClient in ActiveClients.GetAll ().Values)
        {
            if (otherClient is null || IsAttackerTargetId (attacker, otherClient.EntityID) ||
                IsAttackerTargetId (attacker, otherClient.EntityID) || !seen.Add (otherClient.EntityID))
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

            yield return otherClient.EntityID;
        }
    }

    /// <summary>
    /// Aimed object, or the attacker when the aim is self; a missing non-self aim aborts splash
    /// </summary>
    private static bool TryResolveAoeCenter (SphereClient attacker, Guid mainTargetEntityId, out Vector3 center)
    {
        if (IsAttackerTargetId (attacker, mainTargetEntityId))
        {
            return TryGetCombatWorldPosition (attacker, out center);
        }

        var mainObject = ActiveWorldObjects.Get (mainTargetEntityId);
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

    private static Guid ReadGuidAtBit (byte[] frame, int bit)
    {
        if (frame.Length * 8 < bit + 128)
        {
            return Guid.Empty;
        }

        var stream = new BitStream (frame);
        stream.ReadBits (bit);
        return stream.ReadGuid ();
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
