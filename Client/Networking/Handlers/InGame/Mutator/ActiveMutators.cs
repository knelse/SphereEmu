using System;
using System.Collections.Generic;
using System.Linq;
using SphereHelpers.Extensions;
using SphServer.Packets;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Networking;
using SphServer.Shared.WorldState;
using static SphServer.Shared.BitStream.SphBitStream;

namespace SphServer.Client.Networking.Handlers.InGame.Mutator;

public static class ActiveMutators
{
    private sealed record Effect (ushort ItemId, ushort MutatorId, BelongingSlot Slot,
        DateTime Until, bool Beneficial);

    private static readonly Dictionary<int, List<Effect>> Effects = [];

    public static void Register (ushort clientId, ushort itemId, ushort mutatorId, BelongingSlot slot,
        int seconds, bool beneficial)
    {
        if (ActiveClients.Get (clientId)?.CurrentCharacter is not { } character)
        {
            return;
        }

        if (!Effects.TryGetValue (character.Id, out var effects))
        {
            Effects[character.Id] = effects = [];
        }
        effects.RemoveAll (x => x.Slot == slot);
        effects.Add (new Effect (itemId, mutatorId, slot, DateTime.UtcNow.AddSeconds (seconds), beneficial));
    }

    public static bool HandsBound (CharacterDbEntry character) =>
        Effects.TryGetValue (character.Id, out var effects) &&
        effects.Any (x => x.MutatorId == 1036 && x.Until > DateTime.UtcNow);

    public static bool ApplyShackles (SphereClient target, int seconds, ushort casterId)
    {
        if (target.CurrentCharacter is not { } character)
        {
            return false;
        }

        Expire (character.Id);
        var effects = Effects.GetValueOrDefault (character.Id) ?? [];
        if (effects.FirstOrDefault (x => x.MutatorId == 1036) is { } old)
        {
            Remove (character.Id, old);
        }
        var slot = Enumerable.Range ((int) BelongingSlot.Mutator_1, 10)
            .Select (x => (BelongingSlot) x)
            .Where (x => effects.All (e => e.Slot != x))
            .Cast<BelongingSlot?> ().FirstOrDefault ();
        if (slot is null)
        {
            return false;
        }

        var itemId = WorldObjectIndex.NewItem ();
        Register (character.ClientIndex, itemId, 1036, slot.Value, seconds, false);
        Send (target, itemId, 1036, slot.Value, seconds, casterId);
        target.GetTree ().CreateTimer (seconds).Timeout += () => Expire (character.Id);
        return true;
    }

    public static int Purify (SphereClient target, int count)
    {
        if (target.CurrentCharacter is not { } character || !Effects.TryGetValue (character.Id, out var effects))
        {
            return 0;
        }
        Expire (character.Id);
        var removed = effects.Where (x => x.Beneficial).Take (count).ToList ();
        foreach (var effect in removed)
        {
            Remove (character.Id, effect);
        }
        return removed.Count;
    }

    public static void Restore (SphereClient client)
    {
        if (client.CurrentCharacter is not { } character || !Effects.TryGetValue (character.Id, out var effects))
        {
            return;
        }
        Expire (character.Id);
        foreach (var effect in effects.Where (x => x.MutatorId == 1036))
        {
            Send (client, effect.ItemId, effect.MutatorId, effect.Slot,
                Math.Max (1, (int) (effect.Until - DateTime.UtcNow).TotalSeconds), character.ClientIndex);
        }
    }

    private static void Expire (int characterId)
    {
        if (!Effects.TryGetValue (characterId, out var effects))
        {
            return;
        }
        foreach (var effect in effects.Where (x => x.Until <= DateTime.UtcNow).ToList ())
        {
            Remove (characterId, effect);
        }
    }

    private static void Remove (int characterId, Effect effect)
    {
        Effects[characterId].Remove (effect);
        var client = ActiveClients.GetAll ().Values.FirstOrDefault (x => x.CurrentCharacter?.Id == characterId);
        if (client?.CurrentCharacter is { } character)
        {
            if (ItemSlotReserve.Build (character.ClientIndex, effect.Slot, ItemSlotReserve.NoItem) is { } clear)
            {
                client.MaybeQueueNetworkPacketSend (clear);
            }
            client.MaybeQueueNetworkPacketSend (CommonPackets.DespawnEntity (effect.ItemId));
        }
        WorldObjectIndex.Release (effect.ItemId);
    }

    private static void Send (SphereClient target, ushort itemId, ushort mutatorId, BelongingSlot slot,
        int seconds, ushort casterId)
    {
        // A bare ContMan spawn has no region 6 record, so the effect worked but its slot had no
        // icon; the spawn now goes as for any special mutator, and the setup after it sets the real
        // duration and caster
        foreach (var packet in MutatorHandler.BuildSpecialMutatorPackets (target.localId, itemId,
                     (SpecialMutator) mutatorId, slot))
        {
            target.MaybeQueueNetworkPacketSend (packet);
        }
        target.MaybeQueueNetworkPacketSend (BuildSetup (itemId, mutatorId, seconds, ByteSwap (casterId)));
    }

    internal static byte[] BuildSetup (ushort itemId, ushort mutatorId, int seconds, ushort casterId)
    {
        // virus.RcvUser: mutator id u16, mode u8, remaining seconds u16, caster u16
        var stream = GetWriteBitStream ();
        stream.WriteUInt16 (itemId, 16);
        stream.WriteByte (0, 2);
        stream.WriteUInt16 ((ushort) ObjectType.Mutator, 12);
        stream.WriteByte (5, 7);
        stream.WriteByte (15, 8);
        stream.WriteByte (7, 8);
        stream.WriteUInt16 (mutatorId, 16);
        stream.WriteByte (0, 8);
        stream.WriteUInt16 ((ushort) Math.Clamp (seconds, 1, 29699), 16);
        stream.WriteUInt16 (casterId, 16);
        stream.WriteByte (0, 7);
        return Packet.ToByteArray (stream.GetStreamData (), 3);
    }
}
