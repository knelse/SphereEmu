using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SphServer.Client;

namespace SphServer.Shared.WorldState;

internal abstract class ActiveClients : ActiveObjectCollectionBase<ushort, SphereClient>
{
    internal static ushort InsertAtFirstEmptyIndex (SphereClient value)
    {
        // TODO: this is horribly inefficient
        var index = 0x4F6F;
        for (ushort i = 0x4F6F; i < ushort.MaxValue; i++)
        {
            if (!storage.ContainsKey(i))
            {
                index = i;
                break;
            }
        }

        if (index == -1)
        {
            throw new ArgumentException("Reached max number of connections");
        }

        Add((ushort) index, value);

        return (ushort) index;
    }

    internal static SphereClient? FirstOrDefault ()
    {
        return storage.Values.FirstOrDefault();
    }
}

internal abstract class ActiveNodes : ActiveObjectCollectionBase<ulong, Node>;

// internal abstract class ActiveWorldObjects : ActiveObjectCollectionBase<ushort, WorldObject>;

internal abstract class ActiveObjectCollectionBase<Tk, Tv>
{
    protected static readonly ConcurrentDictionary<Tk, Tv> storage = new ();

    public static Tv? Get (Tk key)
    {
        return storage.GetValueOrDefault(key);
    }

    public static void Add (Tk key, Tv value)
    {
        storage[key] = value;
    }

    public static Tv? Remove (Tk key)
    {
        storage.TryRemove(key, out var value);
        return value;
    }
}