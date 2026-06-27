using System;
using System.Collections.Generic;
using Godot;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
///     Caches mesh triangle soups for repeated terrain walk-surface raycasts during editor bakes.
/// </summary>
internal static class TerrainMeshRaycastCache
{
    private static readonly Dictionary<ulong, CachedMeshTriangles> Cache = new();
    private static readonly object CacheLock = new();

    public static void Clear()
    {
        lock (CacheLock)
        {
            Cache.Clear();
        }
    }

    public static void PrewarmMesh(Mesh mesh)
    {
        _ = GetOrCreateCached(mesh);
    }

    public const float DefaultMinWalkableNormalY = 0.55f;

    public static bool TryRaycastMesh(
        Mesh mesh,
        Transform3D globalTransform,
        Vector3 fromWorld,
        Vector3 toWorld,
        out Vector3 hitWorld,
        out float fraction)
    {
        hitWorld = default;
        fraction = float.MaxValue;

        var cached = GetOrCreateCached(mesh);
        return cached.TryRaycast(globalTransform, fromWorld, toWorld, out hitWorld, out fraction);
    }

    public static bool TryRaycastMeshTopWalkableSurface(
        Mesh mesh,
        Transform3D globalTransform,
        Vector3 fromWorld,
        Vector3 toWorld,
        float minNormalY,
        out Vector3 hitWorld)
    {
        hitWorld = default;
        var cached = GetOrCreateCached(mesh);
        return cached.TryRaycastTopWalkableSurface(globalTransform, fromWorld, toWorld, minNormalY, out hitWorld);
    }

    private static CachedMeshTriangles GetOrCreateCached(Mesh mesh)
    {
        lock (CacheLock)
        {
            if (!Cache.TryGetValue(mesh.GetInstanceId(), out var cached))
            {
                cached = CachedMeshTriangles.FromMesh(mesh);
                Cache[mesh.GetInstanceId()] = cached;
            }

            return cached;
        }
    }

    private sealed class CachedMeshTriangles
    {
        private readonly Vector3[] _v0;
        private readonly Vector3[] _v1;
        private readonly Vector3[] _v2;

        private CachedMeshTriangles(Vector3[] v0, Vector3[] v1, Vector3[] v2)
        {
            _v0 = v0;
            _v1 = v1;
            _v2 = v2;
        }

        public static CachedMeshTriangles FromMesh(Mesh mesh)
        {
            var triangles = new List<(Vector3 A, Vector3 B, Vector3 C)>(4096);
            for (var surfaceIndex = 0; surfaceIndex < mesh.GetSurfaceCount(); surfaceIndex++)
            {
                var arrays = mesh.SurfaceGetArrays(surfaceIndex);
                var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                if (vertices.Length == 0)
                {
                    continue;
                }

                var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                if (indices.Length >= 3)
                {
                    for (var i = 0; i + 2 < indices.Length; i += 3)
                    {
                        triangles.Add((vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]]));
                    }

                    continue;
                }

                for (var i = 0; i + 2 < vertices.Length; i += 3)
                {
                    triangles.Add((vertices[i], vertices[i + 1], vertices[i + 2]));
                }
            }

            var v0 = new Vector3[triangles.Count];
            var v1 = new Vector3[triangles.Count];
            var v2 = new Vector3[triangles.Count];
            for (var i = 0; i < triangles.Count; i++)
            {
                v0[i] = triangles[i].A;
                v1[i] = triangles[i].B;
                v2[i] = triangles[i].C;
            }

            return new CachedMeshTriangles(v0, v1, v2);
        }

        public bool TryRaycast(
            Transform3D globalTransform,
            Vector3 fromWorld,
            Vector3 toWorld,
            out Vector3 hitWorld,
            out float fraction)
        {
            hitWorld = default;
            fraction = float.MaxValue;

            var inv = globalTransform.AffineInverse();
            var fromLocal = inv * fromWorld;
            var toLocal = inv * toWorld;
            var dirLocal = toLocal - fromLocal;
            var rayLength = dirLocal.Length();
            if (rayLength < 0.0001f)
            {
                return false;
            }

            dirLocal /= rayLength;
            var found = false;
            var bestT = float.MaxValue;

            for (var i = 0; i < _v0.Length; i++)
            {
                if (!TerrainWalkMeshRaycast.TryRayTriangle(
                        fromLocal,
                        dirLocal,
                        rayLength,
                        _v0[i],
                        _v1[i],
                        _v2[i],
                        out var t)
                    || t >= bestT)
                {
                    continue;
                }

                bestT = t;
                found = true;
            }

            if (!found)
            {
                return false;
            }

            fraction = bestT / rayLength;
            hitWorld = globalTransform * (fromLocal + dirLocal * bestT);
            return true;
        }

        public bool TryRaycastTopWalkableSurface(
            Transform3D globalTransform,
            Vector3 fromWorld,
            Vector3 toWorld,
            float minNormalY,
            out Vector3 hitWorld)
        {
            hitWorld = default;

            var inv = globalTransform.AffineInverse();
            var fromLocal = inv * fromWorld;
            var toLocal = inv * toWorld;
            var dirLocal = toLocal - fromLocal;
            var rayLength = dirLocal.Length();
            if (rayLength < 0.0001f)
            {
                return false;
            }

            dirLocal /= rayLength;
            var found = false;
            var bestT = float.MaxValue;

            for (var i = 0; i < _v0.Length; i++)
            {
                if (!TerrainWalkMeshRaycast.TryRayTriangle(
                        fromLocal,
                        dirLocal,
                        rayLength,
                        _v0[i],
                        _v1[i],
                        _v2[i],
                        out var t)
                    || t >= bestT)
                {
                    continue;
                }

                var localNormal = (_v1[i] - _v0[i]).Cross(_v2[i] - _v0[i]);
                if (localNormal.LengthSquared() < 1e-12f)
                {
                    continue;
                }

                localNormal = localNormal.Normalized();
                var worldNormal = (globalTransform.Basis * localNormal).Normalized();
                if (worldNormal.Y < minNormalY)
                {
                    continue;
                }

                bestT = t;
                found = true;
            }

            if (!found)
            {
                return false;
            }

            hitWorld = globalTransform * (fromLocal + dirLocal * bestT);
            return true;
        }
    }
}
