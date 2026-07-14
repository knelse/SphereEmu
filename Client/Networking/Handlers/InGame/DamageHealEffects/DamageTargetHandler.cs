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

/// <summary>Delivery-layer roll result (miss/crit applied on top of the base damage formula).</summary>
public readonly record struct MeleeHitRoll (int Damage, bool IsMiss, bool IsCrit);

// Fallback branch of first bytes 0x19/0x20: every frame that is not a buy-item request
// (08 40 03, routed to BuyItemFromTargetHandler first) lands here.
public class DamageTargetHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    // Per-connection RNG: handlers run on the Godot main thread and Random is not thread-safe.
    private readonly Random combatRng = new ();

    private int droppedNotAnAttackCount;
    private int droppedSelfTargetCount;
    private bool notAnAttackDropLogged;
    private bool selfTargetDropLogged;

    public Task Handle (double delta)
    {
        // Attack-wedge fix (#11): ack the use first — before any parse or early return — so the
        // client's use-lock (g_6008) is always cleared. See CommonPackets.ClearUseToutAck.
        clientConnection.MaybeScheduleNetworkPacketSend(CommonPackets.ClearUseToutAck(localId));

        var character = clientConnection.GetSelectedCharacter();
        if (character is null)
        {
            return Task.CompletedTask;
        }

        var cfg = BalanceConfig.Get<CombatBalance>("combat");

        var frameKind = ParseAttackFrame(clientConnection.ReceiveBuffer, out var targetClientLocalId);

        switch (frameKind)
        {
            case AttackFrameKind.SelfTargetedAction:
                droppedSelfTargetCount++;
                if (!selfTargetDropLogged)
                {
                    selfTargetDropLogged = true;
                    SphLogger.Info("op45 self-targeted action (Alt; 54 43 C1 at bytes 13-15) dropped — self-attack is a no-op, " +
                                   "self-casts (mantras) are not implemented yet. " +
                                   $"Counted per session, logged once. Client ID: {localId:X4}");
                }

                return Task.CompletedTask;

            case AttackFrameKind.WeaponAttack:
                // Weapon target/damage parse unresolved — echo a 0-damage swing so the client renders it.
                var weaponStream = new BitStream(clientConnection.ReceiveBuffer);
                weaponStream.ReadBits(172);
                var weaponEchoTarget = weaponStream.ReadUInt16();
                clientConnection.MaybeScheduleNetworkPacketSend(
                    CommonPackets.FistAttackTargetEcho(weaponEchoTarget, character.ClientIndex, 0));
                return Task.CompletedTask;

            case AttackFrameKind.NotAnAttack:
                droppedNotAnAttackCount++;
                if (!notAnAttackDropLogged)
                {
                    notAnAttackDropLogged = true;
                    var interactBuffer = clientConnection.ReceiveBuffer;
                    SphLogger.Info("op45 non-attack cuse frame absorbed with NO reply (discriminator " +
                                   $"{interactBuffer[13]:X2} {interactBuffer[14]:X2} {interactBuffer[15]:X2} — not the " +
                                   "08 40 A3 melee attack; e.g. 08 40 83 = right-click object-interact/enter). " +
                                   "Only genuine attacks get a melee reply, so client object interaction " +
                                   $"is not corrupted. Counted per session, logged once. Client ID: {localId:X4}");
                }

                // No echo: an attack reply to an object-interact can corrupt the client's own handling.
                // Dead-mob left-clicks also land here; their use-lock is cleared by the ack at the top.
                return Task.CompletedTask;
        }

        var attackerClient = ActiveClients.Get(localId);

        // Bit-172 id is client-local; ActiveWorldObjects is keyed by global ids (identity-mapped for now).
        var targetGlobalId = attackerClient?.GetGlobalObjectId(targetClientLocalId) ?? targetClientLocalId;

        if (targetClientLocalId == ushort.MaxValue)
        {
            // Target-less attack (0xFFFF = the client's no-target sentinel; happens when the target
            // despawns mid-click). Never answer it: an entity-addressed echo to a nonexistent id
            // crashes the client's script VM (BoundCheckArray in _player, observed live).
            return Task.CompletedTask;
        }

        if (ActiveWorldObjects.Get(targetGlobalId) is not Monster monster ||
            !IsWithinMeleeRange(attackerClient, monster, cfg))
        {
            // Not a live in-range Monster — echo 0 damage to keep the client's swing visual consistent.
            clientConnection.MaybeScheduleNetworkPacketSend(
                CommonPackets.FistAttackTargetEcho(targetClientLocalId, character.ClientIndex, 0));
            return Task.CompletedTask;
        }

        if (monster.IsDead)
        {
            // Echo 0 damage with no state change — keeps the swing loop alive until the corpse despawns.
            clientConnection.MaybeScheduleNetworkPacketSend(
                CommonPackets.FistAttackTargetEcho(targetClientLocalId, character.ClientIndex, 0));
            return Task.CompletedTask;
        }

        var roll = RollMeleeHit(character.PAtk, monster.BasePDef, combatRng, cfg);
        var damageEvent = new DamageEvent(character.ClientIndex, attackerClient, roll.Damage,
            DamageSchool.Physical, roll.IsCrit);
        var outcome = monster.TakeDamage(in damageEvent);

        // fist_attack_target encodes 30000 - damage (client applies raw - 30000 to target HP);
        // echo the APPLIED damage so the client's HP delta matches the server clamp.
        clientConnection.MaybeScheduleNetworkPacketSend(
            CommonPackets.FistAttackTargetEcho(targetClientLocalId, character.ClientIndex, outcome.Applied));
        return Task.CompletedTask;
    }

    /// <summary>Classifies a non-buy 0x19/0x20 frame; the fist-attack target id = LSB-first u16 after 172 skipped bits.</summary>
    public static AttackFrameKind ParseAttackFrame (byte[] receiveBuffer, out ushort targetClientLocalId)
    {
        targetClientLocalId = 0;

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
            var receiveStream = new BitStream(receiveBuffer);
            receiveStream.ReadBits(172);
            targetClientLocalId = receiveStream.ReadUInt16();
            return AttackFrameKind.FistAttack;
        }

        return AttackFrameKind.NotAnAttack;
    }

    /// <summary>
    ///     Melee delivery layer: miss roll, then the damage formula, then crit (re-clamped), then
    ///     the non-miss floor. Fixed rng draw order keeps seeded tests deterministic.
    /// </summary>
    public static MeleeHitRoll RollMeleeHit (int attackerPAtk, double targetPDef, Random rng, CombatBalance cfg)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(cfg);

        var missRoll = rng.NextDouble();
        if (missRoll < cfg.MissChance)
        {
            return new MeleeHitRoll(0, true, false);
        }

        var statSheetDamage = Math.Abs(attackerPAtk) + cfg.FistStatSheetDamage;
        var schoolInput = new DamageSchoolInput(statSheetDamage, cfg.FistAmin, cfg.FistAmax, targetPDef);
        var damage = DamageFormula.RollSchoolDamage(in schoolInput, rng, cfg);

        var critRoll = rng.NextDouble();
        var isCrit = critRoll < cfg.CritChance;
        if (isCrit)
        {
            damage = (int) Math.Min(Math.Floor(damage * cfg.CritMult), cfg.DamageClampMax);
        }

        damage = Math.Max(damage, cfg.MinMeleeHit);
        return new MeleeHitRoll(damage, false, isCrit);
    }

    /// <summary>Lenient server-side range sanity bound; skipped when the attacker position or a positive configured range is unavailable.</summary>
    private static bool IsWithinMeleeRange (SphereClient? attackerClient, Monster monster, CombatBalance cfg)
    {
        if (cfg.MeleeRangeMeters <= 0 || attackerClient is null ||
            !ClientWorldPosition.TryGetGodotWorldPosition(attackerClient, out var attackerPosition))
        {
            return true;
        }

        var rangeSquared = cfg.MeleeRangeMeters * cfg.MeleeRangeMeters;
        return monster.GlobalPosition.DistanceSquaredTo(attackerPosition) <= rangeSquared;
    }
}
