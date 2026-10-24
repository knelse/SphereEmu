using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SphServer.Client;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Shared.WorldState;

internal abstract class ActiveClients : ActiveObjectCollectionBase<Guid, SphereClient>
{
    internal static void Insert (SphereClient value)
    {
        if (value.EntityID == Guid.Empty)
        {
            value.EntityID = Guid.NewGuid ();
        }

        Add (value.EntityID, value);
        ClientStateEvents.RaiseRosterChanged ();
    }

    internal static SphereClient? FirstOrDefault ()
    {
        return storage.Values.FirstOrDefault ();
    }

    internal static ConcurrentDictionary<Guid, SphereClient> GetAll ()
    {
        return storage;
    }

    public new static SphereClient? Remove (Guid key)
    {
        var removed = ActiveObjectCollectionBase<Guid, SphereClient>.Remove (key);
        if (removed is not null)
        {
            ClientStateEvents.RaiseRosterChanged ();
        }

        return removed;
    }
}

internal abstract class ActiveNodes : ActiveObjectCollectionBase<ulong, Node>
{
    internal static ConcurrentDictionary<ulong, Node> GetAll ()
    {
        return storage;
    }
}

internal abstract class ActiveWorldObjects : ActiveObjectCollectionBase<Guid, WorldObject>
{
    internal static ConcurrentDictionary<Guid, WorldObject> GetAll ()
    {
        return storage;
    }
}

internal abstract class ActiveObjectCollectionBase<Tk, Tv> where Tk : notnull
{
    protected static readonly ConcurrentDictionary<Tk, Tv> storage = new ();

    public static readonly ConcurrentDictionary<string, int> LoggedInClients = new ();

    public static Tv? Get (Tk key)
    {
        return storage.GetValueOrDefault (key);
    }

    public static void Add (Tk key, Tv value)
    {
        storage[key] = value;
    }

    public static Tv? Remove (Tk key)
    {
        storage.TryRemove (key, out var value);
        return value;
    }
}
