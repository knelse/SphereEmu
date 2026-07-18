#if TOOLS
using System.Diagnostics;
using Godot;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

/// <summary>Editor-only scene save for bake-all checkpoints (keeps GodotSharpEditor out of headless paths).</summary>
public static class EditorSpawnSlotBakeCheckpoint
{
    public static void TrySave(int processed, int totalDirty, Stopwatch stopwatch)
    {
        if (!Engine.IsEditorHint())
        {
            return;
        }

        try
        {
            var editor = EditorInterface.Singleton;
            editor.MarkSceneAsUnsaved();
            var err = editor.SaveScene();
            if (err != Error.Ok)
            {
                GD.PushWarning(
                    $"MonsterSpawnSlotBaker: progress save failed ({err}) after "
                    + $"{processed}/{totalDirty} dirty spawner(s).");
                return;
            }

            GD.Print(
                $"MonsterSpawnSlotBaker: saved scene progress "
                + $"({processed}/{totalDirty} dirty spawner(s), {stopwatch.Elapsed.TotalSeconds:0.0}s)");
        }
        catch (global::System.Exception ex)
        {
            GD.PushWarning($"MonsterSpawnSlotBaker: progress save threw ({ex.Message}).");
        }
    }
}
#else
using System.Diagnostics;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

/// <summary>Export/headless stub — editor scene saves are unavailable outside TOOLS builds.</summary>
public static class EditorSpawnSlotBakeCheckpoint
{
    public static void TrySave(int processed, int totalDirty, Stopwatch stopwatch)
    {
    }
}
#endif
