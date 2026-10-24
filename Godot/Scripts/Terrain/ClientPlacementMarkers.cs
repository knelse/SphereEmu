using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
/// The client's spawn rule per placement, read from params/sst*.mbd. A record with code >= 10000 is
/// a marker: instantiateStaticGroups stores it as a special point and, with markers hidden, spawns
/// no scene object, so the client collides on nothing there. Two skips follow: a model that is only
/// ever a marker (char19, edoor, npc29, pyram1, …) never blocks anywhere, and a placement sitting on
/// a marker position of a model the client spawns (ct_chest3, cc103, …) blocks nowhere in the world
/// at that spot. The .mbd carries no interior (maze, room, guildhall) placements, so those models
/// stay judged on the .mdl. Client data is outside the repo, the root ClientModelCollision reads
/// </summary>
public static class ClientPlacementMarkers
{
    private const int RecordSize = 44;
    private const int GroupCount = 500;
    private const int MarkerCode = 10000;

    // The JSON and .mbd frames agree on the same game coordinates, compared at 5 cm for float32
    // rounding: each axis becomes an int index, and the key carries three of them
    private const float QuantizeStep = 0.05f;
    private const int QuantizeBits = 21;
    private const long QuantizeBias = 1L << QuantizeBits;

    private static readonly HashSet<string> SpawnedNames = new (StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> MarkerNames = new (StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, HashSet<long>> SpawnedAt = new (StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, HashSet<long>> MarkerAt = new (StringComparer.OrdinalIgnoreCase);
    private static bool scanned;

    /// <summary>
    /// True when the client spawns no object at this placement: the model is marker-only, or this
    /// exact spot is a marker of a model spawned elsewhere. godotPlacement is the placement origin
    /// </summary>
    public static bool IsMarkerOnly (string objectName, Vector3 godotPlacement)
    {
        EnsureScan ();
        if (string.IsNullOrWhiteSpace (objectName))
        {
            return false;
        }

        if (MarkerNames.Contains (objectName) && !SpawnedNames.Contains (objectName))
        {
            return true;
        }

        var source = SourceCoordinates (godotPlacement);
        return HasAt (MarkerAt, objectName, source) && !HasAt (SpawnedAt, objectName, source);
    }

    /// <summary>
    /// The JSON and .mbd frame from a placement origin: TerrainObjectPlacementSource maps source
    /// (x, y, z) to godot (x, -y, -z)
    /// </summary>
    private static Vector3 SourceCoordinates (Vector3 godotPlacement) =>
        new (godotPlacement.X, -godotPlacement.Y, -godotPlacement.Z);

    private static bool HasAt (Dictionary<string, HashSet<long>> table, string objectName, Vector3 source)
    {
        if (!table.TryGetValue (objectName, out var keys))
        {
            return false;
        }

        var x = Quantize (source.X);
        var y = Quantize (source.Y);
        var z = Quantize (source.Z);
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dz = -1; dz <= 1; dz++)
                {
                    if (keys.Contains (Key (x + dx, y + dy, z + dz)))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static int Quantize (float value) => Mathf.RoundToInt (value / QuantizeStep);

    private static long Key (int x, int y, int z) =>
        ((x + QuantizeBias) << (QuantizeBits * 2)) | ((y + QuantizeBias) << QuantizeBits) | (z + QuantizeBias);

    private static void EnsureScan ()
    {
        if (scanned)
        {
            return;
        }

        scanned = true;
        var directory = Path.Combine (ClientModelCollision.ClientDataRoot, "params");
        if (!Directory.Exists (directory))
        {
            GD.PushWarning ($"ClientPlacementMarkers: no client params dir {directory}");
            return;
        }

        var records = 0;
        for (var id = 0; id < GroupCount; id++)
        {
            // The client reads exactly these names, and decode() only: a file whose size is not
            // 4 + 44*count is skipped there, so it is skipped here
            // D2/D3, not 02/03: a bare 02 is the placeholder 0 then a literal 2
            var name = id < 100 ? $"sst_{id:D2}.mbd" : $"sst{id:D3}.mbd";
            var path = Path.Combine (directory, name);
            if (!File.Exists (path))
            {
                continue;
            }

            var bytes = File.ReadAllBytes (path);
            if (bytes.Length < 4)
            {
                continue;
            }

            var count = BitConverter.ToInt32 (bytes, 0);
            if (count < 0 || bytes.Length - 4 < (long) count * RecordSize)
            {
                continue;
            }

            for (var index = 0; index < count; index++)
            {
                var offset = 4 + index * RecordSize;
                var model = ModelName (bytes, offset);
                if (model.Length == 0)
                {
                    continue;
                }

                var x = BitConverter.ToSingle (bytes, offset + 20);
                var y = BitConverter.ToSingle (bytes, offset + 24);
                var z = BitConverter.ToSingle (bytes, offset + 28);
                var key = Key (Quantize (x), Quantize (y), Quantize (z));
                if (BitConverter.ToUInt16 (bytes, offset + 18) >= MarkerCode)
                {
                    MarkerNames.Add (model);
                    AddAt (MarkerAt, model, key);
                }
                else
                {
                    SpawnedNames.Add (model);
                    AddAt (SpawnedAt, model, key);
                }

                records++;
            }
        }

        GD.Print (
            $"ClientPlacementMarkers: records={records} spawned={SpawnedNames.Count} markers={MarkerNames.Count}");
    }

    private static void AddAt (Dictionary<string, HashSet<long>> table, string model, long key)
    {
        if (!table.TryGetValue (model, out var keys))
        {
            keys = [];
            table[model] = keys;
        }

        keys.Add (key);
    }

    private static string ModelName (byte[] bytes, int offset)
    {
        var end = offset;
        while (end < offset + 18 && bytes[end] != 0)
        {
            end++;
        }

        return Encoding.ASCII.GetString (bytes, offset, end - offset).ToLowerInvariant ();
    }
}
