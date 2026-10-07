using Godot;

namespace SphServer.Sphere.Game.WorldObject;

/// <summary>
/// Real on-disk casing, .scn before .glb, because a mismatched path poisons NTFS and case-sensitive
/// exports
/// </summary>
internal static class GlbModelPaths
{
    private static readonly object Gate = new ();
    private static readonly Dictionary<string, string> FileNameByLower = new (StringComparer.Ordinal);
    private static string IndexedDirectory = string.Empty;

    private static readonly string[] Extensions = [".scn", ".glb", ".gltf"];

    /// <summary>
    /// Actual on-disk casing, or null when the file is missing
    /// </summary>
    public static string? Resolve (
        string modelName,
        string modelsDirectory = GlbVisualGrounding.DefaultModelsDirectory)
    {
        if (string.IsNullOrWhiteSpace (modelName))
        {
            return null;
        }

        var dir = modelsDirectory.TrimEnd ('/') + "/";
        EnsureIndex (dir);

        foreach (var ext in Extensions)
        {
            var key = (modelName + ext).ToLowerInvariant ();
            if (FileNameByLower.TryGetValue (key, out var actualRelative))
            {
                return dir + actualRelative;
            }
        }

        return null;
    }

    /// <summary>
    /// Like <see cref="Resolve" />, but falls back to a naive <c>{name}.scn</c> path when missing.
    /// </summary>
    public static string ResolveOrFallback (
        string modelName,
        string modelsDirectory = GlbVisualGrounding.DefaultModelsDirectory)
    {
        return Resolve (modelName, modelsDirectory)
               ?? $"{modelsDirectory.TrimEnd ('/')}/{modelName}.scn";
    }

    private static void EnsureIndex (string dirWithSlash)
    {
        lock (Gate)
        {
            if (IndexedDirectory == dirWithSlash && FileNameByLower.Count > 0)
            {
                return;
            }

            FileNameByLower.Clear ();
            IndexedDirectory = dirWithSlash;
            IndexDirectory (dirWithSlash, relativePrefix: "");
        }
    }

    private static void IndexDirectory (string dirWithSlash, string relativePrefix)
    {
        using var da = DirAccess.Open (dirWithSlash);
        if (da is null)
        {
            return;
        }

        da.ListDirBegin ();
        while (true)
        {
            var name = da.GetNext ();
            if (string.IsNullOrEmpty (name))
            {
                break;
            }

            if (name.StartsWith ('.'))
            {
                continue;
            }

            if (da.CurrentIsDir ())
            {
                IndexDirectory ($"{dirWithSlash}{name}/", $"{relativePrefix}{name}/");
                continue;
            }

            var lower = name.ToLowerInvariant ();
            var isModel =
                lower.EndsWith (".scn", StringComparison.Ordinal)
                || lower.EndsWith (".glb", StringComparison.Ordinal)
                || lower.EndsWith (".gltf", StringComparison.Ordinal);
            if (!isModel)
            {
                continue;
            }

            // Filename only, because callers pass the model name without a folder
            FileNameByLower[lower] = relativePrefix + name;
        }
    }
}
