using System;
using System.Collections.Generic;
using Godot;
using SphServer.Godot.Scripts.Util;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Terrain;

/// <summary>
///     Headless: writes ModelBounds/index.json, the model-space visual AABB for every model in
///     Godot/Models. The server builds the player box shape from it, so the capsule matches the
///     client's model bounds collision. Run: <c>.\Tools\pack_model_bounds.ps1</c>
/// </summary>
public partial class ModelBoundsPackHeadless : Node
{
    public const string DefaultModelsDirectory = GlbVisualGrounding.DefaultModelsDirectory;

    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;

    public static bool IsActive { get; private set; }

    public override void _Ready ()
    {
        // GlobalTransform needs the tree, so the pass runs on frame 1
        SetProcess (true);
    }

    public override void _Process (double delta)
    {
        SetProcess (false);
        IsActive = true;
        var code = ExitSuccess;
        try
        {
            code = Run ();
        }
        finally
        {
            IsActive = false;
            GetTree ().CallDeferred (SceneTree.MethodName.Quit, code);
        }
    }

    private int Run ()
    {
        TerrainBakePaths.EnsureBakeRoot ();
        TerrainBakePaths.EnsureDirectoryExactCase (TerrainBakePaths.Combine ("ModelBounds"));

        var entries = new List<(string Name, ModelBoundsIndex.Bounds Bounds)> ();
        var missing = 0;
        foreach (var name in ModelNames ())
        {
            var path = GlbModelPaths.Resolve (name, DefaultModelsDirectory);
            if (path is null || !ResourceLoader.Exists (path))
            {
                missing++;
                continue;
            }

            if (ResourceLoader.Load<PackedScene> (path)?.Instantiate<Node3D> () is not { } root)
            {
                missing++;
                continue;
            }

            AddChild (root);
            var bounds = Measure (root);
            root.GetParent ()?.RemoveChild (root);
            root.Free ();
            if (bounds is null)
            {
                missing++;
                continue;
            }

            entries.Add ((name, bounds));
        }

        var indexPath = TerrainBakePaths.ModelBoundsIndexJson;
        ModelBoundsIndex.Save (indexPath, entries);
        GD.Print ($"ModelBoundsPackHeadless: models={entries.Count} skipped={missing} index={indexPath}");
        var sample = entries.Count > 0 ? entries[0] : default;
        if (sample.Name is not null)
        {
            GD.Print (
                $"ModelBoundsPackHeadless: sample {sample.Name} "
                + $"size={sample.Bounds.SizeVector} min={sample.Bounds.MinVector} "
                + $"aboveFeet={sample.Bounds.AboveFeet:0.00} centerY={sample.Bounds.CenterY:0.00}");
        }

        return entries.Count > 0 ? ExitSuccess : ExitFailure;
    }

    /// <summary>
    /// Every model stem in the models directory. GlbModelPaths.Resolve handles the casing
    /// </summary>
    private static IEnumerable<string> ModelNames ()
    {
        var seen = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
        foreach (var file in ResPathIO.EnumerateFileNames (DefaultModelsDirectory))
        {
            var dot = file.LastIndexOf ('.');
            if (dot <= 0)
            {
                continue;
            }

            var ext = file[dot..];
            if (ext is not (".scn" or ".glb" or ".gltf"))
            {
                continue;
            }

            var stem = file[..dot];
            if (seen.Add (stem))
            {
                yield return stem;
            }
        }
    }

    private ModelBoundsIndex.Bounds? Measure (Node3D root)
    {
        Aabb total = default;
        var has = false;
        foreach (var node in root.FindChildren ("*", nameof (MeshInstance3D), recursive: true, owned: false))
        {
            if (node is not MeshInstance3D { Mesh: { } mesh } instance)
            {
                continue;
            }

            var aabb = TransformAabb (mesh.GetAabb (), root.GlobalTransform.AffineInverse () * instance.GlobalTransform);
            total = has ? total.Merge (aabb) : aabb;
            has = true;
        }

        if (!has || total.Size.Y <= 0.01f)
        {
            return null;
        }

        return new ModelBoundsIndex.Bounds
        {
            Size = [total.Size.X, total.Size.Y, total.Size.Z],
            Min = [total.Position.X, total.Position.Y, total.Position.Z],
        };
    }

    private static Aabb TransformAabb (Aabb aabb, Transform3D transform)
    {
        var result = new Aabb (transform * aabb.GetEndpoint (0), Vector3.Zero);
        for (var i = 1; i < 8; i++)
        {
            result = result.Expand (transform * aabb.GetEndpoint (i));
        }

        return result;
    }
}
