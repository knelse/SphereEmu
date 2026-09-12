using System;
using System.Threading.Tasks;
using SphServer.Client.Networking.Handlers.InGame.DamageHealEffects;
using SphServer.Server.Config;
using SphServer.Server.GameplayLogic.Combat;
using SphServer.Shared.ClientEvents;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Client.EventHandlers;

public sealed class CombatHitEventHandler(SphereClient sphereClient) : IClientEventHandler
{
    private readonly Random combatRng = new();

    public Task HandleAsync(CombatHitEvent clientEvent)
    {
        var character = sphereClient.CurrentCharacter;
        if (character is null)
        {
            LogAction(clientEvent.AttackerGlobalId, clientEvent.TargetGlobalId, clientEvent.FrameKind,
                "skip-no-character");
            return Task.CompletedTask;
        }

        var cfg = BalanceConfig.Get<CombatBalance>("combat");
        if (cfg is null)
        {
            LogAction(clientEvent.AttackerGlobalId, clientEvent.TargetGlobalId, clientEvent.FrameKind, "skip");
            return Task.CompletedTask;
        }

        var targetObject = ActiveWorldObjects.Get(clientEvent.TargetGlobalId);
        if (targetObject is SphereClient)
        {
            sphereClient.MaybeQueueNetworkPacketSend(
                CommonPackets.AttackTargetEcho(clientEvent.TargetLocalId, character.ClientIndex, 0, 0,
                    ObjectType.Player));
            LogAction(clientEvent.AttackerGlobalId, clientEvent.TargetGlobalId, clientEvent.FrameKind,
                "player-stub");
            return Task.CompletedTask;
        }

        if (targetObject is not Monster monster)
        {
            sphereClient.MaybeQueueNetworkPacketSend(
                CommonPackets.AttackTargetEcho(clientEvent.TargetLocalId, character.ClientIndex, 0, 0));
            LogAction(clientEvent.AttackerGlobalId, clientEvent.TargetGlobalId, clientEvent.FrameKind,
                targetObject is null ? "skip-no-such-object" : $"skip-not-a-monster-{targetObject.GetType().Name}");
            return Task.CompletedTask;
        }

        var currentHealth = monster.CurrentHp;
        var targetObjectType = WireObjectType(monster);

        if (monster.IsDead)
        {
            sphereClient.MaybeQueueNetworkPacketSend(
                CommonPackets.AttackTargetEcho(clientEvent.TargetLocalId, character.ClientIndex, 0, 0,
                    targetObjectType));
            LogAction(clientEvent.AttackerGlobalId, clientEvent.TargetGlobalId, clientEvent.FrameKind,
                "skip-already-dead");
            return Task.CompletedTask;
        }

        var meleeRoll = DamageCalc.RollMeleeHit(character.MeleePAtk, !character.HoldsItemInHand,
            monster.CurrentPDef, combatRng, cfg);
        var magicRoll = DamageCalc.RollMagicHit(character.MagicMAtk, !character.HoldsItemInHand,
            monster.CurrentMDef, combatRng, cfg);

        var damageSchool = magicRoll.Damage <= meleeRoll.Damage ? DamageSchool.Physical : DamageSchool.Magical;
        var damageEvent = new DamageEvent(clientEvent.AttackerGlobalId, sphereClient,
            meleeRoll.Damage + magicRoll.Damage, damageSchool, meleeRoll.IsCrit || magicRoll.IsCrit);
        var outcome = monster.TakeDamage(in damageEvent);
        if (outcome.BecameDead)
        {
            LogAction(clientEvent.AttackerGlobalId, clientEvent.TargetGlobalId, clientEvent.FrameKind, "kill");
            return Task.CompletedTask;
        }

        var damageApplied = outcome.Applied;
        if (damageApplied is < 0 or > 30000)
        {
            SphLogger.Error($"CombatHit: damage {damageApplied} outside 0..30000, clamped.");
            damageApplied = Math.Clamp(damageApplied, 0, 30000);
        }

        var damageToTarget = 30000 - damageApplied;
        currentHealth -= damageApplied;
        sphereClient.MaybeQueueNetworkPacketSend(
            CommonPackets.AttackTargetEcho(clientEvent.TargetLocalId, character.ClientIndex, damageToTarget,
                currentHealth, targetObjectType));
        LogAction(clientEvent.AttackerGlobalId, clientEvent.TargetGlobalId, clientEvent.FrameKind,
            meleeRoll.IsMiss && magicRoll.IsMiss ? "miss" : outcome.Applied.ToString());
        return Task.CompletedTask;
    }

    Task IClientEventHandler.HandleAsync(ClientQueuedEvent clientEvent) =>
        HandleAsync((CombatHitEvent)clientEvent);

    private static ObjectType WireObjectType(Monster monster)
    {
        return monster.DataObjectType is GameObjectType.Monster_Flying or GameObjectType.Monster_Event_Flying
            or GameObjectType.Special_Necromancer_Flyer
            ? ObjectType.MonsterFlyer
            : ObjectType.Monster;
    }

    private static void LogAction(ushort sourceGlobalId, ushort targetGlobalId, AttackFrameKind action, string result)
    {
        SphLogger.Info($"DamageTargetHandler: Source [{sourceGlobalId:X4}] - Target [{targetGlobalId:X4}] - " +
                       $"Action [{action}] - [{result}]");
    }
}
