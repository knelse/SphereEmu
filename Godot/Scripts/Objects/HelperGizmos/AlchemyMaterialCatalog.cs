using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

/// <summary>
///     Indexes alchemy material GameObject IDs from <see cref="SphObjectDb" /> by
///     <see cref="GameObjectType" /> (Flower / Metal / Mineral), with Russian display names
///     for editor enum pickers.
/// </summary>
public static class AlchemyMaterialCatalog
{
    private static readonly object BuildLock = new();
    private static Dictionary<GameObjectType, HashSet<int>>? _idsByType;
    private static Dictionary<int, string>? _russianNamesById;
    private static Dictionary<GameObjectType, string>? _enumHintByType;

    public static IReadOnlyList<int> GetIds(GameObjectType type)
    {
        EnsureBuilt();
        if (_idsByType is null || !_idsByType.TryGetValue(type, out var set))
        {
            return [];
        }

        return set.OrderBy(id => id).ToList();
    }

    public static bool TryGetType(int gameObjectId, out GameObjectType type)
    {
        EnsureBuilt();
        type = default;
        if (_idsByType is null)
        {
            return false;
        }

        foreach (var (candidateType, ids) in _idsByType)
        {
            if (ids.Contains(gameObjectId))
            {
                type = candidateType;
                return true;
            }
        }

        return false;
    }

    public static bool IsValidMaterialId(int gameObjectId)
        => TryGetType(gameObjectId, out _);

    public static string GetRussianName(int gameObjectId)
    {
        EnsureBuilt();
        if (_russianNamesById is not null && _russianNamesById.TryGetValue(gameObjectId, out var name)
            && !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return $"#{gameObjectId}";
    }

    /// <summary>
    ///     Godot <see cref="PropertyHint.Enum" /> hint for int arrays: <c>Name:id,Name2:id2</c>.
    ///     Stored values remain GameObject IDs.
    /// </summary>
    public static string GetEnumHintString(GameObjectType type)
    {
        EnsureBuilt();
        if (_enumHintByType is not null && _enumHintByType.TryGetValue(type, out var hint))
        {
            return hint;
        }

        return string.Empty;
    }

    public static ObjectType ToNetworkObjectType(GameObjectType type)
        => type switch
        {
            GameObjectType.Flower => ObjectType.AlchemyPlant,
            GameObjectType.Metal => ObjectType.AlchemyMetal,
            GameObjectType.Mineral => ObjectType.AlchemyMineral,
            _ => ObjectType.AlchemyPlant,
        };

    public static void Invalidate()
    {
        lock (BuildLock)
        {
            _idsByType = null;
            _russianNamesById = null;
            _enumHintByType = null;
        }
    }

    private static void EnsureBuilt()
    {
        if (_idsByType is not null)
        {
            return;
        }

        lock (BuildLock)
        {
            if (_idsByType is not null)
            {
                return;
            }

            var map = new Dictionary<GameObjectType, HashSet<int>>
            {
                [GameObjectType.Flower] = [],
                [GameObjectType.Metal] = [],
                [GameObjectType.Mineral] = [],
            };
            var names = new Dictionary<int, string>();

            foreach (var (dbId, entry) in SphObjectDb.GameObjectDataDb)
            {
                if (entry.GameObjectType is not (GameObjectType.Flower or GameObjectType.Metal or GameObjectType.Mineral))
                {
                    continue;
                }

                map[entry.GameObjectType].Add(dbId);
                names[dbId] = ResolveRussianName(entry, dbId);
            }

            _idsByType = map;
            _russianNamesById = names;
            _enumHintByType = new Dictionary<GameObjectType, string>
            {
                [GameObjectType.Flower] = BuildEnumHint(map[GameObjectType.Flower], names),
                [GameObjectType.Metal] = BuildEnumHint(map[GameObjectType.Metal], names),
                [GameObjectType.Mineral] = BuildEnumHint(map[GameObjectType.Mineral], names),
            };

            GD.Print(
                $"AlchemyMaterialCatalog: plants={map[GameObjectType.Flower].Count} "
                + $"metals={map[GameObjectType.Metal].Count} minerals={map[GameObjectType.Mineral].Count}");
        }
    }

    private static string ResolveRussianName(SphGameObject entry, int dbId)
    {
        if (entry.Localisation is not null
            && entry.Localisation.TryGetValue(Locale.Russian, out var russian)
            && !string.IsNullOrWhiteSpace(russian))
        {
            return russian.Trim();
        }

        if (!string.IsNullOrWhiteSpace(entry.SphereType))
        {
            return entry.SphereType.Trim();
        }

        return $"#{dbId}";
    }

    private static string BuildEnumHint(HashSet<int> ids, Dictionary<int, string> names)
    {
        if (ids.Count == 0)
        {
            return string.Empty;
        }

        var ordered = ids.OrderBy(id => names.GetValueOrDefault(id, $"#{id}"), global::System.StringComparer.Ordinal)
            .ThenBy(id => id)
            .ToList();

        var nameCounts = new Dictionary<string, int>(global::System.StringComparer.Ordinal);
        foreach (var id in ordered)
        {
            var key = SanitizeHintLabel(names.GetValueOrDefault(id, $"#{id}"));
            nameCounts[key] = nameCounts.GetValueOrDefault(key) + 1;
        }

        var seen = new Dictionary<string, int>(global::System.StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (var id in ordered)
        {
            var baseLabel = SanitizeHintLabel(names.GetValueOrDefault(id, $"#{id}"));
            var label = baseLabel;
            if (nameCounts.GetValueOrDefault(baseLabel) > 1)
            {
                var n = seen.GetValueOrDefault(baseLabel) + 1;
                seen[baseLabel] = n;
                label = $"{baseLabel} [{id}]";
            }

            if (sb.Length > 0)
            {
                sb.Append(',');
            }

            sb.Append(label);
            sb.Append(':');
            sb.Append(id);
        }

        return sb.ToString();
    }

    /// <summary>
    ///     Enum hint uses ',' as option separator and ':' as name/value separator.
    /// </summary>
    private static string SanitizeHintLabel(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "?";
        }

        return raw.Trim()
            .Replace(',', ' ')
            .Replace(':', ' ')
            .Replace('\n', ' ')
            .Replace('\r', ' ');
    }
}
