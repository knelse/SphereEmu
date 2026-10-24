using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
/// The client's collision rule per model, read from the .mdl header. collision_kind u32 at 26: 0
/// axis-aligned box from min 30 / max 42, 1 oriented box from 8 corners at 54, 2 triangles, 3
/// nothing. The box axes match the .scn model frame, checked on cci11: corners 2.4, 3.0, 0.1 against
/// ModelBounds 2.378, 3.078, 0.0
/// </summary>
public static class ClientModelCollision
{
    /// <summary>
    /// The vendored client data root (Godot/Models/original), shared by the .mdl scan and the
    /// params/sst*.mbd placement scan. GlobalizePath so System.IO reads the repo tree
    /// </summary>
    public static string ClientDataRoot =>
        ProjectSettings.GlobalizePath ("res://Godot/Models/original");

    public const int AxisAlignedBounds = 0;
    public const int OrientedBounds = 1;
    public const int Triangles = 2;
    public const int None = 3;

    /// <summary>
    /// No .mdl, so the client never creates the object and collides nothing on it
    /// </summary>
    public const int NoModel = -1;

    private const int CollisionKindOffset = 26;
    private const int MinOffset = 30;
    private const int MaxOffset = 42;
    private const int CornersOffset = 54;
    private const int CornerCount = 8;

    private sealed record Entry (int Kind, Vector3 Size, Vector3 Center, bool HasBox);

    private static readonly Dictionary<string, Entry> ByModel = new (StringComparer.OrdinalIgnoreCase);
    private static bool scanned;

    public static int Kind (string modelName) =>
        TryModel (modelName, out var entry) ? entry.Kind : NoModel;

    /// <summary>
    /// Box the client stops an actor on, in the model's own frame. False for a triangles model,
    /// which blocks on its mesh, and for a model with no usable box
    /// </summary>
    public static bool TryGetBox (string modelName, out Vector3 size, out Vector3 center)
    {
        size = center = Vector3.Zero;
        if (!TryModel (modelName, out var entry) || !entry.HasBox)
        {
            return false;
        }

        size = entry.Size;
        center = entry.Center;
        return size.X > 0f && size.Y > 0f && size.Z > 0f;
    }

    private static bool TryModel (string modelName, out Entry entry)
    {
        entry = null!;
        if (string.IsNullOrWhiteSpace (modelName))
        {
            return false;
        }

        EnsureScan ();
        if (!ByModel.TryGetValue (modelName, out var found))
        {
            return false;
        }

        entry = found;
        return true;
    }

    private static void EnsureScan ()
    {
        if (scanned)
        {
            return;
        }

        scanned = true;
        foreach (var directory in ModelDirectories ())
        {
            if (!Directory.Exists (directory))
            {
                GD.PushWarning ($"ClientModelCollision: no client model dir {directory}");
                continue;
            }

            foreach (var file in Directory.EnumerateFiles (directory, "*.mdl", SearchOption.AllDirectories))
            {
                var stem = Path.GetFileNameWithoutExtension (file).ToLowerInvariant ();
                if (!ByModel.ContainsKey (stem) && ReadEntry (file) is { } entry)
                {
                    ByModel[stem] = entry;
                }
            }
        }

        GD.Print ($"ClientModelCollision: entries for {ByModel.Count} models");
    }

    private static IEnumerable<string> ModelDirectories ()
    {
        yield return Path.Combine (ClientDataRoot, "models");
        yield return Path.Combine (ClientDataRoot, "Models_hr");
        yield return Path.Combine (ClientDataRoot, "Models_ph");
        yield return Path.Combine (ClientDataRoot, "Models_rd");
    }

    private static Entry? ReadEntry (string path)
    {
        try
        {
            var bytes = File.ReadAllBytes (path);
            if (bytes.Length <= CollisionKindOffset + 3)
            {
                return null;
            }

            var kind = (int) BitConverter.ToUInt32 (bytes, CollisionKindOffset);
            if (kind > None)
            {
                return null;
            }

            // Kind 0 blocks on min/max, kind 1 on the oriented corners, and a kind 1 min/max is empty
            return kind switch
            {
                AxisAlignedBounds => BoxEntry (bytes, kind, MinOffset, MaxOffset),
                OrientedBounds => CornersEntry (bytes, kind),
                _ => new Entry (kind, Vector3.Zero, Vector3.Zero, false)
            };
        }
        catch (Exception ex)
        {
            GD.PushWarning ($"ClientModelCollision: read failed {Path.GetFileName (path)}: {ex.Message}");
            return null;
        }
    }

    private static Entry BoxEntry (byte[] bytes, int kind, int minOffset, int maxOffset)
    {
        if (bytes.Length <= maxOffset + 11)
        {
            return new Entry (kind, Vector3.Zero, Vector3.Zero, false);
        }

        return BoxFrom (kind, ReadPoint (bytes, minOffset), ReadPoint (bytes, maxOffset));
    }

    /// <summary>
    /// The oriented box, axis aligned in the model frame, so the placement rotation supplies the
    /// orientation the client builds from its own euler
    /// </summary>
    private static Entry CornersEntry (byte[] bytes, int kind)
    {
        if (bytes.Length < CornersOffset + CornerCount * 12)
        {
            return new Entry (kind, Vector3.Zero, Vector3.Zero, false);
        }

        var min = new Vector3 (float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3 (float.MinValue, float.MinValue, float.MinValue);
        for (var index = 0; index < CornerCount; index++)
        {
            var corner = ReadPoint (bytes, CornersOffset + index * 12);
            min = min.Min (corner);
            max = max.Max (corner);
        }

        return BoxFrom (kind, min, max);
    }

    /// <summary>
    /// Game Y is down, Godot Y is up, so Y flips and Z does not: a model whose game Y spans -1.8 to
    /// 0 lands on a box rising 1.8 from the placement origin, the char*.mdl feet-below-origin case
    /// </summary>
    private static Entry BoxFrom (int kind, Vector3 min, Vector3 max)
    {
        var size = max - min;
        if (size.X <= 0f || size.Y <= 0f || size.Z <= 0f)
        {
            return new Entry (kind, Vector3.Zero, Vector3.Zero, false);
        }

        var center = new Vector3 (
            (min.X + max.X) * 0.5f,
            -(min.Y + max.Y) * 0.5f,
            (min.Z + max.Z) * 0.5f);
        return new Entry (kind, size, center, true);
    }

    private static Vector3 ReadPoint (byte[] bytes, int offset) =>
        new (
            BitConverter.ToSingle (bytes, offset),
            BitConverter.ToSingle (bytes, offset + 4),
            BitConverter.ToSingle (bytes, offset + 8));
}
