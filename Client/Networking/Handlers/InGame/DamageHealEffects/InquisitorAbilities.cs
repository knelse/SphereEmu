using System;
using System.Linq;
using SphServer.Client.Networking.Handlers.InGame.Mutator;
using SphServer.Helpers;
using SphServer.Shared.BitStream;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking.Chat.Encoders;
using SphServer.Shared.Networking.DataModel.Serializers;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Client.Networking.Handlers.InGame.DamageHealEffects;

public static class InquisitorAbilities
{
    public static bool IsAbility (int gameId) =>
        gameId is >= 5710 and <= 5715 or >= 5720 and <= 5725 or >= 5730 and <= 5735;

    public static bool TryUse (SphereClient caster, ItemDbEntry ability, ushort targetId)
    {
        if (!IsAbility (ability.GameId))
        {
            return false;
        }
        if (caster.CurrentCharacter is not { } character || character.Guild != Guild.Inquisitor ||
            !character.Items.ContainsValue (ability.Id))
        {
            return true;
        }

        if (ability.NextAbilityUseAt is { } ready && ready > DateTime.UtcNow)
        {
            Tell (caster, $"Способность восстанавливается: {Math.Ceiling ((ready - DateTime.UtcNow).TotalMinutes)} мин.");
            return true;
        }

        var target = targetId == caster.localId || targetId == SphBitStream.ByteSwap (caster.localId)
            ? caster
            : ActiveClients.Get (targetId) as WorldObject ?? ActiveWorldObjects.Get (targetId);
        // Inquisitor abilities act on characters only, not on monsters
        if (target is not SphereClient)
        {
            Tell (caster, "Способность применяется к персонажу.");
            return true;
        }
        if (target.GlobalPosition.DistanceTo (character.Origin) > Math.Max (1, ability.Range) + 0.5f)
        {
            Tell (caster, "Цель слишком далеко.");
            return true;
        }

        var success = false;
        if (ability.GameId is >= 5710 and <= 5715)
        {
            if (target is SphereClient { CurrentCharacter: { CurrentHP: > 0 } victim } targetClient)
            {
                // On oneself it is a return to the nearest city, on another player a send to the PK respawn
                var self = targetClient == caster;
                var point = self
                    ? NearestCityPoint (victim)
                    : SavedCoords.RespawnPoints[Continents.Гиперион][Cities.Санпул][KarmaTypes.Очень_Плохая];
                targetClient.MaybeQueueNetworkPacketSend (new CharacterDbEntrySerializer (victim).GetTeleportByteArray (point));
                victim.X = point.x;
                victim.Y = -point.y;
                victim.Z = -point.z;
                victim.Angle = point.turn;
                targetClient.SaveCharacter ();
                targetClient.UpdateCoordinatesInWorld ();
                Tell (caster, self
                    ? "Возрождение: вы перенесены в ближайший город."
                    : "Возрождение: цель перенесена к месту появления персонажей с очень плохой кармой.");
                success = true;
            }
            else
            {
                Tell (caster, "Возрождение применяется к живому персонажу.");
            }
        }
        else if (ability.GameId is >= 5720 and <= 5725)
        {
            var seconds = EffectDurationSeconds (ability._duration);
            success = target switch
            {
                SphereClient { CurrentCharacter.CurrentHP: > 0 } player =>
                    ActiveMutators.ApplyShackles (player, seconds, caster.localId),
                _ => false
            };
            Tell (caster, success ? $"Оковы: {seconds / 60} мин." : "Не удалось наложить Оковы.");
        }
        else
        {
            var count = PurifyCount (ability.MutatorId);
            var removed = target is SphereClient player ? ActiveMutators.Purify (player, count) : 0;
            Tell (caster, $"Очищение: снято положительных эффектов: {removed}.");
            success = true;
        }

        if (success)
        {
            ability.NextAbilityUseAt = DateTime.UtcNow.AddHours (Math.Max (1, ability.ReuseDelayHours));
            DbConnection.Items.Update (ability);
            SphLogger.Info ($"Inquisitor ability {ability.GameId}: {character.ClientIndex:X4} -> {targetId:X4}");
        }
        return true;
    }

    /// <summary>
    /// Effect duration column of the params cfg tables, as the specab info card prints it: below 100 hours, 100..1000 minutes
    /// over 100, above 1000 seconds over 1000. Shackles 104..120 are 4..20 minutes
    /// </summary>
    public static int EffectDurationSeconds (int raw) => raw switch
    {
        > 1000 => raw - 1000,
        >= 100 => (raw - 100) * 60,
        _ => Math.Max (1, raw) * 3600
    };

    /// <summary>
    /// Purification ranks carry mutator 1024, 1025 or 1026, which virus describes as removing 2, 4 or 6 effects
    /// </summary>
    private static int PurifyCount (int mutatorId) => mutatorId switch
    {
        1026 => 6,
        1025 => 4,
        _ => 2
    };

    /// <summary>
    /// Nearest Hyperion city by center; its respawn point for the character's karma when recorded,
    /// else the city center
    /// </summary>
    private static WorldCoords NearestCityPoint (CharacterDbEntry character)
    {
        var (name, center) = SavedCoords.TeleportPoints[Continents.Гиперион][PoiType.CityCenter]
            .OrderBy (c => Math.Pow (c.Value.x - character.X, 2) + Math.Pow (c.Value.z + character.Z, 2))
            .First ();
        return SavedCoords.RespawnPoints[Continents.Гиперион].TryGetValue (Enum.Parse<Cities> (name), out var byKarma)
               && byKarma.TryGetValue (character.Karma, out var point)
            ? point
            : center;
    }

    public static void Tell (SphereClient client, string message) => client.MaybeQueueNetworkPacketSend (
        MessageEncoder.EncodeToSendFromServer (message, "GM", (int) PublicChatType.GM_Outgoing));
}
