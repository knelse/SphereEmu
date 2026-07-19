using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

/// <summary>
///     Proximity-activated alchemy material spawner. Bakes navmesh slots in a green sphere radius;
///     spawns <see cref="AlchemyResource" /> children from configured Flower/Metal/Mineral GameObject IDs.
/// </summary>
[Tool]
public partial class AlchemyMaterialSpawner : Node3D
{
	public const string ScenePath = "res://Godot/Scenes/alchemy_material_spawner.tscn";
	private const string AlchemyResourceScenePath = "res://Godot/Scenes/alchemy_resource.tscn";
	private const string RadiusGizmoNodeName = "RadiusGizmo";
	private const float SlotOccupiedEpsilonMeters = 0.35f;

	private static readonly Random Rng = new();

	[Export]
	public float SpawnRadiusMeters { get; set; } = 15f;

	/// <summary>How many navmesh slots to bake into this spawner.</summary>
	[Export(PropertyHint.Range, "1,256,1")]
	public int SpawnSlotCount { get; set; } = 20;

	/// <summary>Flower GameObject IDs; inspector shows Russian names.</summary>
	[Export]
	public int[] PlantGameObjectIds { get; set; } = [];

	/// <summary>Metal GameObject IDs; inspector shows Russian names.</summary>
	[Export]
	public int[] MetalGameObjectIds { get; set; } = [];

	/// <summary>Mineral GameObject IDs; inspector shows Russian names.</summary>
	[Export]
	public int[] MineralGameObjectIds { get; set; } = [];

	[Export]
	public int RespawnDelaySeconds { get; set; } = 300;

	[Export]
	public int MaxCount { get; set; } = 10;

	[Export]
	public Array<Vector3> BakedSpawnSlots { get; private set; } = [];

	[Export]
	public bool HasBakeError { get; private set; }

	[Export]
	public string BakeErrorDetail { get; private set; } = string.Empty;

	[ExportToolButton("Bake spawn slots")]
	public Callable BakeSpawnSlotsButton => Callable.From(BakeSpawnSlots);

	private bool _activated;
	private readonly List<PendingRespawn> _pendingRespawns = [];
	private readonly HashSet<ulong> _trackedChildIds = [];

	private struct PendingRespawn
	{
		public double ReadyAtSeconds;
	}

	public bool IsActivated => _activated;

	public override void _Ready()
	{
		EnsureRadiusGizmo();
		UpdateRadiusGizmo();

		if (Engine.IsEditorHint())
		{
			return;
		}

		if (MonsterSpawnSlotHeadlessBake.IsActive || AlchemyMaterialSpawnSlotHeadlessBake.IsActive)
		{
			return;
		}

		StripPersistedChildren();
		ChildExitingTree += OnChildExitingTree;
		AlchemyMaterialSpawnerActivationManager.Register(this);
	}

	public override void _ExitTree()
	{
		if (!Engine.IsEditorHint())
		{
			ChildExitingTree -= OnChildExitingTree;
			AlchemyMaterialSpawnerActivationManager.Unregister(this);
		}
	}

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint())
		{
			UpdateRadiusGizmo();
			return;
		}

		if (!_activated)
		{
			return;
		}

		ProcessPendingRespawns();
	}

	/// <summary>
	///     Editor inspector plugin: Russian-name enum hint (<c>Name:id,...</c>) for material ID arrays.
	/// </summary>
	public string GetEditorMaterialPickerHint(string propertyName)
		=> propertyName switch
		{
			nameof(PlantGameObjectIds) => AlchemyMaterialCatalog.GetEnumHintString(GameObjectType.Flower),
			nameof(MetalGameObjectIds) => AlchemyMaterialCatalog.GetEnumHintString(GameObjectType.Metal),
			nameof(MineralGameObjectIds) => AlchemyMaterialCatalog.GetEnumHintString(GameObjectType.Mineral),
			_ => string.Empty,
		};

	/// <summary>
	///     Editor inspector plugin write-back for material ID arrays (avoids PackedInt32Array ↔ int[] edge cases).
	/// </summary>
	public void SetEditorMaterialIds(string propertyName, Variant idsVariant)
	{
		var ids = VariantToIntArray(idsVariant);
		switch (propertyName)
		{
			case nameof(PlantGameObjectIds):
				PlantGameObjectIds = ids;
				break;
			case nameof(MetalGameObjectIds):
				MetalGameObjectIds = ids;
				break;
			case nameof(MineralGameObjectIds):
				MineralGameObjectIds = ids;
				break;
		}

		NotifyPropertyListChanged();
	}

	private static int[] VariantToIntArray(Variant idsVariant)
	{
		switch (idsVariant.VariantType)
		{
			case Variant.Type.PackedInt32Array:
				return idsVariant.AsInt32Array();
			case Variant.Type.Array:
				{
					var arr = idsVariant.AsGodotArray();
					var ids = new int[arr.Count];
					for (var i = 0; i < arr.Count; i++)
					{
						ids[i] = arr[i].AsInt32();
					}

					return ids;
				}
			default:
				return [];
		}
	}

	/// <summary>
	///     Used by the editor gizmo plugin; Godot Get() does not reliably return exported arrays on C# nodes.
	/// </summary>
	public Array<Vector3> GetEditorBakedSpawnSlots() => BakedSpawnSlots;

	internal void RefreshEditorGizmo()
	{
		if (Engine.IsEditorHint())
		{
			UpdateGizmos();
		}
	}

	public override void _Notification(int what)
	{
		if (Engine.IsEditorHint() && what == NotificationTransformChanged)
		{
			UpdateGizmos();
		}

		if (what == NotificationChildOrderChanged || what == NotificationEnterTree)
		{
			return;
		}

		base._Notification(what);
	}

	public override bool _Set(StringName property, Variant value)
	{
		if (!base._Set(property, value))
		{
			return false;
		}

		RefreshEditorGizmo();
		return true;
	}

	public void BakeSpawnSlots()
	{
		_ = AlchemyMaterialSpawnSlotBaker.BakeForSpawnerAsync(this);
	}

	internal void SetBakedSpawnSlots(IReadOnlyList<Vector3> slots)
	{
		BakedSpawnSlots.Clear();
		foreach (var slot in slots)
		{
			BakedSpawnSlots.Add(slot);
		}

		HasBakeError = false;
		BakeErrorDetail = string.Empty;
		RefreshEditorGizmo();
	}

	internal void MarkBakeError(string detail)
	{
		HasBakeError = true;
		BakeErrorDetail = detail ?? string.Empty;
		BakedSpawnSlots.Clear();
		RefreshEditorGizmo();
	}

	internal void ActivateFromProximity()
	{
		if (Engine.IsEditorHint() || _activated || HasBakeError)
		{
			return;
		}

		if (BakedSpawnSlots.Count == 0)
		{
			GD.PushWarning($"AlchemyMaterialSpawner '{Name}': no baked slots — run Bake spawn slots.");
			return;
		}

		if (CollectConfiguredIds().Count == 0)
		{
			GD.PushWarning($"AlchemyMaterialSpawner '{Name}': no Plant/Metal/Mineral IDs configured.");
			return;
		}

		_activated = true;
		FillToMaxCount();
		// Same tick as activate: ensure clients already standing here get show packets.
		WorldObjectVisibilityManager.CheckAllClients();
	}

	private void StripPersistedChildren()
	{
		foreach (var child in GetChildren().ToArray())
		{
			if (child is AlchemyResource)
			{
				child.QueueFree();
			}
		}

		_trackedChildIds.Clear();
	}

	private void FillToMaxCount()
	{
		var live = CountLiveResources();
		while (live < MaxCount)
		{
			if (!TrySpawnOne())
			{
				break;
			}

			live++;
		}
	}

	private void ProcessPendingRespawns()
	{
		if (_pendingRespawns.Count == 0)
		{
			return;
		}

		var now = Time.GetTicksMsec() / 1000.0;
		var remaining = new List<PendingRespawn>();
		foreach (var pending in _pendingRespawns)
		{
			if (now < pending.ReadyAtSeconds)
			{
				remaining.Add(pending);
				continue;
			}

			if (CountLiveResources() >= MaxCount)
			{
				remaining.Add(pending);
				continue;
			}

			if (!TrySpawnOne())
			{
				remaining.Add(pending);
			}
		}

		_pendingRespawns.Clear();
		_pendingRespawns.AddRange(remaining);
	}

	private bool TrySpawnOne()
	{
		if (!TryPickFreeSlot(out var slot))
		{
			return false;
		}

		if (!TryPickRandomGameObjectId(out var gameObjectId, out var objectType))
		{
			return false;
		}

		var packed = ResourceLoader.Load<PackedScene>(AlchemyResourceScenePath);
		if (packed is null)
		{
			GD.PushError($"AlchemyMaterialSpawner: failed to load '{AlchemyResourceScenePath}'.");
			return false;
		}

		if (packed.Instantiate() is not AlchemyResource resource)
		{
			GD.PushError("AlchemyMaterialSpawner: alchemy_resource.tscn is not an AlchemyResource.");
			return false;
		}

		resource.GameObjectID = gameObjectId;
		resource.ObjectType = objectType;
		resource.Angle = Rng.Next(0, 64);
		resource.Name = $"Alchemy_{objectType}_{gameObjectId}_{resource.GetInstanceId()}";
		// Scene default is (10000,10000,10000) so Register in _Ready would land in the wrong
		// visibility cell; clear local pose first, then place and re-register at the slot.
		resource.Position = Vector3.Zero;
		AddChild(resource);
		slot.Y += GlbVisualGrounding.GetSpawnOriginYOffsetForGameObjectId(gameObjectId, slot.Y);
		resource.GlobalPosition = slot;
		WorldObjectVisibilityManager.Unregister(resource);
		WorldObjectVisibilityManager.Register(resource);
		_trackedChildIds.Add(resource.GetInstanceId());
		return true;
	}

	private void OnChildExitingTree(Node node)
	{
		if (node is not AlchemyResource resource)
		{
			return;
		}

		var instanceId = resource.GetInstanceId();
		if (!_trackedChildIds.Remove(instanceId))
		{
			return;
		}

		if (!_activated || Engine.IsEditorHint())
		{
			return;
		}

		var delay = Math.Max(0, RespawnDelaySeconds);
		_pendingRespawns.Add(new PendingRespawn
		{
			ReadyAtSeconds = Time.GetTicksMsec() / 1000.0 + delay,
		});
	}

	private int CountLiveResources()
	{
		var count = 0;
		foreach (var child in GetChildren())
		{
			if (child is AlchemyResource resource && GodotObject.IsInstanceValid(resource))
			{
				count++;
			}
		}

		return count;
	}

	private bool TryPickFreeSlot(out Vector3 slot)
	{
		slot = default;
		if (BakedSpawnSlots.Count == 0)
		{
			return false;
		}

		var occupied = new List<Vector3>();
		foreach (var child in GetChildren())
		{
			if (child is AlchemyResource resource && GodotObject.IsInstanceValid(resource))
			{
				occupied.Add(resource.GlobalPosition);
			}
		}

		var free = new List<Vector3>();
		var epsSq = SlotOccupiedEpsilonMeters * SlotOccupiedEpsilonMeters;
		foreach (var candidate in BakedSpawnSlots)
		{
			var taken = false;
			foreach (var occ in occupied)
			{
				var dx = candidate.X - occ.X;
				var dz = candidate.Z - occ.Z;
				if (dx * dx + dz * dz < epsSq)
				{
					taken = true;
					break;
				}
			}

			if (!taken)
			{
				free.Add(candidate);
			}
		}

		if (free.Count == 0)
		{
			return false;
		}

		slot = free[Rng.Next(free.Count)];
		return true;
	}

	private bool TryPickRandomGameObjectId(out int gameObjectId, out ObjectType objectType)
	{
		gameObjectId = 0;
		objectType = ObjectType.AlchemyPlant;
		var pool = CollectConfiguredIds();
		if (pool.Count == 0)
		{
			return false;
		}

		var pick = pool[Rng.Next(pool.Count)];
		gameObjectId = pick.Id;
		objectType = pick.NetworkType;
		return true;
	}

	private List<(int Id, ObjectType NetworkType)> CollectConfiguredIds()
	{
		var pool = new List<(int Id, ObjectType NetworkType)>();
		AddValidated(PlantGameObjectIds, GameObjectType.Flower, pool);
		AddValidated(MetalGameObjectIds, GameObjectType.Metal, pool);
		AddValidated(MineralGameObjectIds, GameObjectType.Mineral, pool);
		return pool;
	}

	private static void AddValidated(
		int[]? ids,
		GameObjectType expectedType,
		List<(int Id, ObjectType NetworkType)> pool)
	{
		if (ids is null || ids.Length == 0)
		{
			return;
		}

		foreach (var id in ids)
		{
			if (id <= 0)
			{
				continue;
			}

			if (!AlchemyMaterialCatalog.TryGetType(id, out var actualType))
			{
				GD.PushWarning($"AlchemyMaterialSpawner: GameObjectID {id} is not a Flower/Metal/Mineral.");
				continue;
			}

			if (actualType != expectedType)
			{
				GD.PushWarning(
					$"AlchemyMaterialSpawner: GameObjectID {id} is {actualType}, expected {expectedType}.");
				continue;
			}

			pool.Add((id, AlchemyMaterialCatalog.ToNetworkObjectType(actualType)));
		}
	}

	private void EnsureRadiusGizmo()
	{
		if (GetNodeOrNull(RadiusGizmoNodeName) is MeshInstance3D)
		{
			return;
		}

		var meshInstance = new MeshInstance3D { Name = RadiusGizmoNodeName };
		var sphere = new SphereMesh
		{
			Radius = 1f,
			Height = 2f,
			RadialSegments = 24,
			Rings = 12,
		};
		var mat = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			AlbedoColor = new Color(0.2f, 0.95f, 0.35f, 0.28f),
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		sphere.Material = mat;
		meshInstance.Mesh = sphere;
		meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		AddChild(meshInstance);
		if (Engine.IsEditorHint())
		{
			meshInstance.Owner = GetTree()?.EditedSceneRoot ?? this;
		}
	}

	private void UpdateRadiusGizmo()
	{
		if (GetNodeOrNull(RadiusGizmoNodeName) is not MeshInstance3D meshInstance)
		{
			return;
		}

		var r = Mathf.Max(0.1f, SpawnRadiusMeters);
		meshInstance.Scale = new Vector3(r, r, r);
		// Editor-only visual; hide at runtime so clients don't see the bake helper.
		meshInstance.Visible = Engine.IsEditorHint();
	}
}
