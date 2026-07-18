using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

/// <summary>
///     Sidecar checkpoint for headless spawn-slot bakes. Survives crashes without re-packing MainServer.tscn.
/// </summary>
public sealed class SpawnSlotBakeProgress
{
    public const string DefaultResourcePath = "res://Godot/Terrain/spawn_slot_bake_progress.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public int Count => _entries.Count;

    public bool Contains(string key) => _entries.ContainsKey(key);

    public static string GetSpawnerKey(MonsterSpawner spawner)
    {
        if (!string.IsNullOrEmpty(spawner.OriginalDisplayName))
        {
            return spawner.OriginalDisplayName;
        }

        var name = spawner.Name.ToString();
        const string errorPrefix = "ERROR - ";
        return name.StartsWith(errorPrefix, StringComparison.Ordinal)
            ? name[errorPrefix.Length..]
            : name;
    }

    public static SpawnSlotBakeProgress LoadOrCreate(string resourcePath)
    {
        var progress = new SpawnSlotBakeProgress();
        var absolute = ProjectSettings.GlobalizePath(resourcePath);
        if (!File.Exists(absolute))
        {
            return progress;
        }

        try
        {
            var json = File.ReadAllText(absolute);
            var file = JsonSerializer.Deserialize<FileModel>(json, JsonOptions);
            if (file?.Results is null)
            {
                return progress;
            }

            foreach (var (key, entry) in file.Results)
            {
                if (string.IsNullOrEmpty(key) || entry is null)
                {
                    continue;
                }

                progress._entries[key] = entry;
            }

            GD.Print($"SpawnSlotBakeProgress: loaded {progress.Count} result(s) from {resourcePath}");
        }
        catch (Exception ex)
        {
            GD.PushWarning($"SpawnSlotBakeProgress: failed to load {resourcePath} ({ex.Message}); starting fresh.");
        }

        return progress;
    }

    public void Save(string resourcePath)
    {
        var absolute = ProjectSettings.GlobalizePath(resourcePath);
        var directory = Path.GetDirectoryName(absolute);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var file = new FileModel { Version = 1, Results = _entries };
        File.WriteAllText(absolute, JsonSerializer.Serialize(file, JsonOptions));
    }

    public void RecordSuccess(string key, IReadOnlyList<Vector3> slots)
    {
        var packed = new List<float>(slots.Count * 3);
        foreach (var slot in slots)
        {
            packed.Add(slot.X);
            packed.Add(slot.Y);
            packed.Add(slot.Z);
        }

        _entries[key] = new Entry { Ok = true, Slots = packed };
    }

    public void RecordFailure(string key)
    {
        _entries[key] = new Entry { Ok = false, Slots = [] };
    }

    public void ApplyToSpawners(Node spawnersParent)
    {
        var applied = 0;
        foreach (var child in spawnersParent.GetChildren())
        {
            if (child is not MonsterSpawner spawner || !GodotObject.IsInstanceValid(spawner))
            {
                continue;
            }

            var key = GetSpawnerKey(spawner);
            if (!_entries.TryGetValue(key, out var entry))
            {
                continue;
            }

            if (entry.Ok)
            {
                spawner.ClearSpawnError();
                spawner.SetBakedSpawnSlots(UnpackSlots(entry.Slots));
            }
            else
            {
                spawner.MarkSpawnError();
                spawner.SetBakedSpawnSlots([]);
            }

            applied++;
        }

        if (applied > 0)
        {
            GD.Print($"SpawnSlotBakeProgress: applied {applied} cached result(s) onto spawners");
        }
    }

    private static List<Vector3> UnpackSlots(IReadOnlyList<float>? packed)
    {
        var slots = new List<Vector3>();
        if (packed is null || packed.Count < 3)
        {
            return slots;
        }

        for (var i = 0; i + 2 < packed.Count; i += 3)
        {
            slots.Add(new Vector3(packed[i], packed[i + 1], packed[i + 2]));
        }

        return slots;
    }

    private sealed class FileModel
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, Entry> Results { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class Entry
    {
        public bool Ok { get; set; }
        public List<float> Slots { get; set; } = [];
    }
}
