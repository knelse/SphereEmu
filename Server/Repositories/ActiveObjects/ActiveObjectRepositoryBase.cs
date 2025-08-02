using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Godot;

namespace SphServer.Repositories;

internal abstract class ActiveClientsRepository : ActiveObjectRepositoryBase<ushort, Client>
{
    internal static ushort InsertAtFirstEmptyIndex (Client value)
    {
        // TODO: this is horribly inefficient
        var index = -1;
        for (ushort i = 1; i < ushort.MaxValue; i++)
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

        Set((ushort) index, value);

        return (ushort) index;
    }
}

internal abstract class ActiveNodesRepository : ActiveObjectRepositoryBase<ulong, Node>;

internal abstract class ActiveWorldObjectRepository : ActiveObjectRepositoryBase<ushort, WorldObject>;

internal abstract class ActiveObjectRepositoryBase<Tk, Tv>
{
    protected static readonly ConcurrentDictionary<Tk, Tv> storage = new ();

    public static Tv? Get (Tk key)
    {
        return storage.GetValueOrDefault(key);
    }

    public static void Set (Tk key, Tv value)
    {
        storage[key] = value;
    }

    public static Tv? Delete (Tk key)
    {
        storage.TryRemove(key, out var value);
        return value;
    }
}