using System;
using System.Threading.Tasks;
using BitStreams;
using SphServer.Server.Config;
using SphServer.Server.GameplayLogic.Combat;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Client.Networking.Handlers.InGame.DamageHealEffects;

/// <summary>Classification of an incoming 0x19/0x20 non-buy frame.</summary>
public enum AttackFrameKind
{
    /// <summary>Bare-hand attack (left click on a live target): 08 40 A3 at bytes 13-15, target id at bits 172-187.</summary>
    FistAttack = 0,

    /// <summary>
    ///     Self-targeted action (Alt modifier: target = the player themselves, hence the player's own
    ///     id at bits 172-187; 54 43 C1 at bytes 13-15). Meant for self-casts like heal mantras;
    ///     a self-targeted fist attack is meaningless, so it is dropped in v1.
    /// </summary>
    SelfTargetedAction = 1,

    /// <summary>
    ///     Weapon swing (7E 14 CE at bytes 22-24): weapons attack via the item-use path, so the frame
    ///     shape differs from <see cref="FistAttack" /> and its target id sits elsewhere.
    /// </summary>
    WeaponAttack = 2,

    /// <summary>Not an attack, e.g. 08 40 83 = right-click interact / use on a dead target — absorbed silently.</summary>
    NotAnAttack = 3
}

// Fallback branch of first bytes 0x19/0x20: every frame that is not a buy-item request
// (08 40 03, routed to BuyItemFromTargetHandler first) lands here.
public class DamageTargetHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    // Per-connection RNG: handlers run on the Godot main thread and Random is not thread-safe.
    private readonly Random combatRng = new ();

    public async Task Handle (double delta)
    {
        // Attack-wedge fix (#11): ack the use first — before any parse or early return — so the
        // client's use-lock (g_6008) is always cleared. See CommonPackets.ClearUseToutAck.
        clientConnection.MaybeScheduleNetworkPacketSend(CommonPackets.ClearUseToutAck(localId));

        var character = clientConnection.GetSelectedCharacter();
        var attackerClient = ActiveClients.Get(localId);
        if (character is null || attackerClient is null)
        {
            return;
        }

        var attackerGlobalId = attackerClient.GetGlobalObjectId(localId);
        var cfg = BalanceConfig.Get<CombatBalance>("combat");
        if (cfg is null)
        {
            LogAction(attackerGlobalId, 0, AttackFrameKind.NotAnAttack, "skip");
            return;
        }

        var frameKind = ParseAttackFrame(clientConnection.ReceiveBuffer, out var targetClientLocalId);

        // 0 = no id in the frame, 0xFFFF = the client's no-target sentinel (target despawned mid-click).
    
        if (targetClientLocalId is 0 or ushort.MaxValue)
        {
            LogAction(attackerGlobalId, targetClientLocalId, frameKind, "skip");
            return;
        }

        var targetGlobalId = attackerClient.GetGlobalObjectId(targetClientLocalId);

        switch (frameKind)
        {
            case AttackFrameKind.SelfTargetedAction:
                // The target is the player themselves; self-casts (mantras) are not implemented yet.
                LogAction(attackerGlobalId, targetGlobalId, frameKind, "skip");
                return;

            case AttackFrameKind.WeaponAttack:
                // Weapon damage parse unresolved — echo a 0-damage swing so the client renders it.
                clientConnection.MaybeScheduleNetworkPacketSend(
                    CommonPackets.FistAttackTargetEcho(targetClientLocalId, character.ClientIndex, 0));
                LogAction(attackerGlobalId, targetGlobalId, frameKind, "0");
                return;

            case AttackFrameKind.NotAnAttack:
                // No echo: an attack reply to an object-interact can corrupt the client's own handling.
                // Dead-mob left-clicks also land here; their use-lock is cleared by the ack at the top.
                LogAction(attackerGlobalId, targetGlobalId, frameKind, "skip");
                return;
        }

        if (ActiveWorldObjects.Get(targetGlobalId) is not Monster monster ||
            !IsWithinMeleeRange(attackerClient, monster, cfg))
        {
            // Not a live in-range Monster — echo 0 damage to keep the client's swing visual consistent.
            clientConnection.MaybeScheduleNetworkPacketSend(
                CommonPackets.FistAttackTargetEcho(targetClientLocalId, character.ClientIndex, 0));
            LogAction(attackerGlobalId, targetGlobalId, frameKind, "skip");
            return;
        }

        if (monster.IsDead)
        {
            // Echo 0 damage with no state change — keeps the swing loop alive until the corpse despawns.
            clientConnection.MaybeScheduleNetworkPacketSend(
                CommonPackets.FistAttackTargetEcho(targetClientLocalId, character.ClientIndex, 0));
            LogAction(attackerGlobalId, targetGlobalId, frameKind, "skip");
            return;
        }

        var roll = DamageCalc.RollMeleeHit(character.PAtk, monster.BasePDef, combatRng, cfg);
        var damageEvent = new DamageEvent(character.ClientIndex, attackerClient, roll.Damage,
            DamageSchool.Physical, roll.IsCrit);
        var outcome = monster.TakeDamage(in damageEvent);

        // fist_attack_target encodes 30000 - damage (client applies raw - 30000 to target HP);
        // echo the APPLIED damage so the client's HP delta matches the server clamp.
        clientConnection.MaybeScheduleNetworkPacketSend(
            CommonPackets.FistAttackTargetEcho(targetClientLocalId, character.ClientIndex, outcome.Applied));
        LogAction(attackerGlobalId, targetGlobalId, frameKind, roll.IsMiss ? "miss" : outcome.Applied.ToString());
    }

    private static void LogAction (ushort sourceGlobalId, ushort targetGlobalId, AttackFrameKind action, string result)
    {
        SphLogger.Info($"DamageTargetHandler: Source [{sourceGlobalId:X4}] - Target [{targetGlobalId:X4}] - " +
                       $"Action [{action}] - [{result}]");
    }

    /// <summary>Classifies a non-buy 0x19/0x20 frame; the target id = LSB-first u16 after 172 skipped bits.</summary>
    public static AttackFrameKind ParseAttackFrame (byte[] receiveBuffer, out ushort targetClientLocalId)
    {
        var receiveStream = new BitStream(receiveBuffer);
        receiveStream.ReadBits(172);
        targetClientLocalId = receiveStream.ReadUInt16();

        if (receiveBuffer[13] == 0x54 && receiveBuffer[14] == 0x43 && receiveBuffer[15] == 0xC1)
        {
            return AttackFrameKind.SelfTargetedAction;
        }

        if (receiveBuffer.Length >= 25 && receiveBuffer[0] >= 25 && receiveBuffer[22] == 0x7E &&
            receiveBuffer[23] == 0x14 && receiveBuffer[24] == 0xCE)
        {
            return AttackFrameKind.WeaponAttack;
        }

        // 08 40 A3 (left-click melee attack) is the ONLY attack discriminator on this route.
        if (receiveBuffer[13] == 0x08 && receiveBuffer[14] == 0x40 && receiveBuffer[15] == 0xA3)
        {
            return AttackFrameKind.FistAttack;
        }

        return AttackFrameKind.NotAnAttack;
    }

    /// <summary>Lenient server-side range sanity bound; skipped when no positive range is configured.</summary>
    private static bool IsWithinMeleeRange (SphereClient attackerClient, Monster monster, CombatBalance cfg)
    {
        if (cfg.MeleeRangeMeters <= 0)
        {
            return true;
        }

        if (!ClientWorldPosition.TryGetGodotWorldPosition(attackerClient, out var attackerPosition))
        {
            // Distance is unknown, so the attack cannot be bounded — reject it instead of allowing it.
            return false;
        }

        var rangeSquared = cfg.MeleeRangeMeters * cfg.MeleeRangeMeters;
        return monster.GlobalPosition.DistanceSquaredTo(attackerPosition) <= rangeSquared;
    }
}
