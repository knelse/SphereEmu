using System;
using System.Collections.Generic;

namespace SphServer.Shared.WorldState;

public static class WorldObjectIndex
{
    public const ushort FirstUsableId = 0x1000;
    public const ushort FirstItemId = 20000;

    private static readonly object Gate = new();
    private static readonly HashSet<ushort> Taken = [];
    private static ushort lastAllocated;

    /// <summary>Last id <see cref="New"/> handed out, or 0 before the first call.</summary>
    public static uint GetCurrentIndex => lastAllocated;

    /// <summary>
    ///     Persistence occupancy (items, containers). Set from <c>DbConnection</c> so this type
    ///     does not take a database dependency at compile time.
    /// </summary>
    public static Func<ushort, bool>? IsPersistedInUse { get; set; }

    public static bool IsTaken(ushort id)
    {
        if (id == 0)
        {
            return false;
        }

        lock (Gate)
        {
            return Taken.Contains(id);
        }
    }

    public static bool IsInUse(ushort id)
    {
        if (id == 0)
        {
            return false;
        }

        if (IsTaken(id))
        {
            return true;
        }

        if (ActiveWorldObjects.Get(id) is not null || ActiveClients.Get(id) is not null)
        {
            return true;
        }

        return IsPersistedInUse?.Invoke(id) == true;
    }

    /// <summary>Marks <paramref name="id"/> as taken. Returns false when it was already reserved.</summary>
    public static bool TryReserve(ushort id)
    {
        if (id == 0)
        {
            return false;
        }

        lock (Gate)
        {
            return Taken.Add(id);
        }
    }

    public static void Reserve(ushort id) => TryReserve(id);

    public static void Release(ushort id)
    {
        if (id == 0)
        {
            return;
        }

        lock (Gate)
        {
            Taken.Remove(id);
        }
    }

    public static void Seed(IEnumerable<ushort> ids)
    {
        lock (Gate)
        {
            foreach (var id in ids)
            {
                if (id != 0)
                {
                    Taken.Add(id);
                }
            }
        }
    }

    /// <summary>Lowest unused wire id from <see cref="FirstUsableId"/>, skipping reserved and live occupants.</summary>
    public static ushort New() => AllocateFrom(FirstUsableId);

    /// <summary>Same as <see cref="New"/> but starts at <see cref="FirstItemId"/> so items stay above baked world ids.</summary>
    public static ushort NewItem() => AllocateFrom(FirstItemId);

    private static ushort AllocateFrom(ushort minId)
    {
        lock (Gate)
        {
            for (var raw = (uint)minId; raw <= ushort.MaxValue; raw++)
            {
                var id = (ushort)raw;
                if (Taken.Contains(id))
                {
                    continue;
                }

                if (ActiveWorldObjects.Get(id) is not null || ActiveClients.Get(id) is not null)
                {
                    Taken.Add(id);
                    continue;
                }

                if (IsPersistedInUse?.Invoke(id) == true)
                {
                    Taken.Add(id);
                    continue;
                }

                Taken.Add(id);
                lastAllocated = id;
                return id;
            }
        }

        throw new InvalidOperationException("Reached max number of world object ids");
    }
}
