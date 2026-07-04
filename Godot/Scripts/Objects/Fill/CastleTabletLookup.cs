using Godot;
using SphServer.Helpers;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects.Fill;

/// <summary>
/// Resolves <see cref="Castles"/> for placements by querying the physics broad phase for
/// <see cref="CastleTablet"/> lookup areas within <see cref="SearchRadiusMeters"/>.
/// </summary>
public static class CastleTabletLookup
{
	/// <summary>Physics layer for <see cref="CastleTablet"/> lookup areas (Godot layer 21).</summary>
	public const uint TabletCollisionLayer = 1u << 20;

	public const float SearchRadiusMeters = 200f;

	private static SphereShape3D? _searchShape;

	public static bool TryGetNearestCastle(Node3D contextNode, Vector3 worldPosition, out Castles castle)
	{
		castle = Castles.UNKNOWN;

		var world = contextNode.GetWorld3D();
		if (world is null)
		{
			return false;
		}

		_searchShape ??= new SphereShape3D { Radius = SearchRadiusMeters };

		var parameters = new PhysicsShapeQueryParameters3D
		{
			Shape = _searchShape,
			Transform = new Transform3D(Basis.Identity, worldPosition),
			CollideWithAreas = true,
			CollideWithBodies = false,
			CollisionMask = TabletCollisionLayer,
		};

		var hits = world.DirectSpaceState.IntersectShape(parameters);

		CastleTablet? nearest = null;
		var nearestDistSq = float.MaxValue;

		foreach (var hit in hits)
		{
			if (!hit.TryGetValue("collider", out var colliderVariant))
			{
				continue;
			}

			var collider = colliderVariant.As<Node>();
			var tablet = FindCastleTablet(collider);
			if (tablet is null)
			{
				continue;
			}

			var distSq = tablet.GlobalPosition.DistanceSquaredTo(worldPosition);
			if (distSq < nearestDistSq)
			{
				nearestDistSq = distSq;
				nearest = tablet;
			}
		}

		if (nearest is null)
		{
			return false;
		}

		castle = nearest.Castle;
		return true;
	}

	private static CastleTablet? FindCastleTablet(Node? node)
	{
		while (node != null)
		{
			if (node is CastleTablet tablet)
			{
				return tablet;
			}

			node = node.GetParent();
		}

		return null;
	}
}
