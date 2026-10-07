using System;
using System.Collections.Generic;
using Godot;
using SphServer.Client;
using SphServer.Server.Config;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.WorldState;

namespace SphServer.Sphere.Game.WorldObject;

public partial class WorldObject
{
    private const int DefaultPositionBroadcastRepeatCount = 4;

    private readonly HashSet<SphereClient> _visibleClients = [];
    private const string VisibilityAreaNodeName = "ClientVisibilityArea";

    private Area3D? _visibilityArea;

    /// <summary>
    /// The Area3D is created on first proximity, not at startup
    /// </summary>
    internal void EnsureVisibilityArea ()
    {
        if (Engine.IsEditorHint () || _visibilityArea is not null)
        {
            return;
        }

        var area3D = new Area3D { Name = VisibilityAreaNodeName };
        area3D.CollisionLayer = 1;
        area3D.CollisionMask = 2;

        var collisionShape3d = new CollisionShape3D ();
        collisionShape3d.Shape = new SphereShape3D
        {
            Radius = ServerConfig.AppConfig.ObjectVisibilityDistance
        };

        area3D.AddChild (collisionShape3d);
        area3D.BodyEntered += OnVisibilityBodyEntered;
        area3D.BodyExited += OnVisibilityBodyExited;
        AddChild (area3D);
        _visibilityArea = area3D;
        CallDeferred (nameof (FlushVisibilityOverlapsDeferred));
    }

    /// <summary>
    /// Fallback when a new area has no physics overlap yet
    /// </summary>
    internal void EnsureVisibleToClient (SphereClient client)
    {
        if (Engine.IsEditorHint () || !GodotObject.IsInstanceValid (client) || client.CurrentCharacter is null)
        {
            return;
        }

        if (_visibleClients.Contains (client))
        {
            return;
        }

        if (!ClientWorldPosition.TryGetGodotWorldPosition (client, out var clientPosition))
        {
            return;
        }

        var visibilityRadius = ServerConfig.AppConfig.ObjectVisibilityDistance;
        if (GlobalPosition.DistanceSquaredTo (clientPosition) > visibilityRadius * visibilityRadius)
        {
            return;
        }

        if (!_visibleClients.Add (client))
        {
            return;
        }

        ShowForClient (client);
    }

    private void OnVisibilityBodyEntered (Node3D body)
    {
        var clientNode = body.GetParent ();
        if (clientNode is not SphereClient client)
        {
            SphLogger.Info ($"WorldObject {Name}: collision enter by {body.Name} which is not a SphereClient");
            return;
        }

        if (!_visibleClients.Add (client))
        {
            return;
        }

        ShowForClient (client);
    }

    private void OnVisibilityBodyExited (Node3D body)
    {
        var clientNode = body.GetParent ();
        if (clientNode is not SphereClient client)
        {
            SphLogger.Info ($"WorldObject {Name}: collision exit by {body.Name} which is not a SphereClient");
            return;
        }

        if (!_visibleClients.Remove (client))
        {
            return;
        }

        client.MaybeQueueNetworkPacketSend (CommonPackets.DespawnEntity (client.GetLocalObjectId (ID)));
    }

    private void FlushVisibilityOverlapsDeferred ()
    {
        if (_visibilityArea is null)
        {
            return;
        }

        foreach (var body in _visibilityArea.GetOverlappingBodies ())
        {
            if (body is Node3D node3D)
            {
                OnVisibilityBodyEntered (node3D);
            }
        }
    }

    /// <summary>
    /// In-place look update; the default sends nothing
    /// </summary>
    public virtual void BroadcastAppearanceRefreshToVisibleClients ()
    {
    }

    protected void ForEachVisibleClient (Action<SphereClient> send)
    {
        if (_visibleClients.Count == 0)
        {
            return;
        }

        var staleClients = new List<SphereClient> ();
        foreach (var client in _visibleClients)
        {
            if (!GodotObject.IsInstanceValid (client))
            {
                staleClients.Add (client);
                continue;
            }

            send (client);
        }

        foreach (var client in staleClients)
        {
            _visibleClients.Remove (client);
        }
    }

    /// <summary>
    /// Same repeat path as the player position event
    /// </summary>
    protected void BroadcastEntityPositionToVisibleClients (
        double gameX,
        double gameY,
        double gameZ,
        double angleRadians,
        int sendCount = DefaultPositionBroadcastRepeatCount)
    {
        if (_visibleClients.Count == 0)
        {
            return;
        }

        var staleClients = new List<SphereClient> ();
        foreach (var client in _visibleClients)
        {
            if (!GodotObject.IsInstanceValid (client))
            {
                staleClients.Add (client);
                continue;
            }

            var entityId = client.GetLocalObjectId (ID);
            for (var i = 0; i < sendCount; i++)
            {
                client.EnqueueClientEvent (
                    new EntityPositionUpdateEvent (entityId, GetMoveModuleTag (), gameX, gameY, gameZ, angleRadians));
            }
        }

        foreach (var client in staleClients)
        {
            _visibleClients.Remove (client);
        }
    }
}
