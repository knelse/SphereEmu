using System.Collections.Generic;
using Godot;
using SphServer.Shared.GameData.Enums;

namespace SphServer.Sphere.Game.WorldObject;

/// <summary>
///     Renders monster GLB models via shared <see cref="MultiMeshInstance3D" /> batches (one batch per
///     <see cref="MonsterType" /> present in the scene). Logic and colliders stay on each <see cref="Monster" /> node.
/// </summary>
public static class MonsterMultiMeshVisuals
{
	private const string ObjectVisualsNodeName = "ObjectVisuals";
	private const string VisualRootNodeName = "MonsterMultiMeshVisuals";
	private const string ModelsDirectory = "res://Godot/Models/";

	private static readonly Dictionary<MonsterType, MonsterTypeVisualBatch> Batches = new();
	private static readonly Dictionary<Monster, MonsterType> RegisteredMonsterTypes = new();
	private static readonly Dictionary<string, List<GroundedMeshPart>?> MeshPartsCache = new();

	private static Node3D? _visualRoot;
	private static int _editorBulkUpdateDepth;
	private static bool _editorRebuildRequested;
	private static SceneTree? _editorRebuildTree;
	private static SceneTree? _activeRebuildTree;
	private static readonly Queue<Action> EditorRebuildSteps = new();
	private static bool _editorRebuildStepPending;
	private static bool _editorRebuildAfterBulk;
	private static int _editorRebuildPostBulkFramesRemaining;
	private static int _editorRebuildZeroMonsterRetries;

	private static bool _editorRebuildStepHandlerActive;
	private static bool _editorDelayedFrameHandlerActive;

	private const int EditorRebuildChunkSize = 500;
	private const int EditorRebuildPostBulkWaitFrames = 3;
	private const int EditorRebuildZeroMonsterMaxRetries = 10;

	public static bool IsBulkEditorUpdate => _editorBulkUpdateDepth > 0;

	public static void BeginBulkEditorUpdate(SceneTree? tree = null)
	{
		if (!Engine.IsEditorHint())
		{
			return;
		}

		_editorBulkUpdateDepth++;
		CancelPendingEditorRebuild();
		ResetEditorVisualState(tree);
	}

	/// <summary>
	///     Drops tracking for a monster being removed during bulk tree edits without touching MultiMesh buffers.
	/// </summary>
	public static void ForgetMonster(Monster monster)
	{
		RegisteredMonsterTypes.Remove(monster);
		foreach (var batch in Batches.Values)
		{
			batch.ForgetMonster(monster);
		}
	}

	private static void ResetEditorVisualState(SceneTree? tree = null)
	{
		Batches.Clear();
		RegisteredMonsterTypes.Clear();
		_visualRoot = null;

		tree ??= Engine.GetMainLoop() as SceneTree;
		var sceneRoot = tree?.EditedSceneRoot;
		if (sceneRoot is null)
		{
			return;
		}

		if (sceneRoot.GetNodeOrNull(ObjectVisualsNodeName) is not Node objectVisuals)
		{
			return;
		}

		if (objectVisuals.GetNodeOrNull(VisualRootNodeName) is not Node3D visualRoot
			|| !GodotObject.IsInstanceValid(visualRoot))
		{
			return;
		}

		_visualRoot = visualRoot;
		foreach (var child in visualRoot.GetChildren())
		{
			if (GodotObject.IsInstanceValid(child))
			{
				child.Free();
			}
		}
	}

	public static void EndBulkEditorUpdate(SceneTree tree)
	{
		if (!Engine.IsEditorHint())
		{
			return;
		}

		if (_editorBulkUpdateDepth <= 0)
		{
			return;
		}

		_editorBulkUpdateDepth--;
		if (_editorBulkUpdateDepth == 0)
		{
			_editorRebuildAfterBulk = true;
			_editorRebuildPostBulkFramesRemaining = EditorRebuildPostBulkWaitFrames;
			_editorRebuildZeroMonsterRetries = 0;
			RequestEditorRebuild(tree);
		}
	}

	public static void RequestEditorRebuild(SceneTree tree)
	{
		if (!Engine.IsEditorHint() || IsBulkEditorUpdate)
		{
			return;
		}

		_editorRebuildTree = tree;
		if (_editorRebuildRequested || _editorDelayedFrameHandlerActive || _editorRebuildStepHandlerActive)
		{
			return;
		}

		_editorRebuildRequested = true;
		tree.ProcessFrame += OnEditorProcessFrame;
	}

	private static void DisconnectEditorProcessFrame(SceneTree? tree)
	{
		if (tree is null || !_editorRebuildRequested)
		{
			return;
		}

		tree.ProcessFrame -= OnEditorProcessFrame;
		_editorRebuildRequested = false;
	}

	private static void ConnectEditorDelayedFrame(SceneTree tree)
	{
		if (_editorDelayedFrameHandlerActive)
		{
			return;
		}

		tree.ProcessFrame += OnEditorProcessFrameDelayed;
		_editorDelayedFrameHandlerActive = true;
	}

	private static void DisconnectEditorDelayedFrame(SceneTree? tree)
	{
		if (tree is null || !_editorDelayedFrameHandlerActive)
		{
			return;
		}

		tree.ProcessFrame -= OnEditorProcessFrameDelayed;
		_editorDelayedFrameHandlerActive = false;
	}

	private static void ConnectEditorRebuildStep(SceneTree tree)
	{
		if (_editorRebuildStepHandlerActive)
		{
			return;
		}

		tree.ProcessFrame += OnEditorRebuildStep;
		_editorRebuildStepHandlerActive = true;
	}

	private static void DisconnectEditorRebuildStep(SceneTree? tree)
	{
		if (tree is null || !_editorRebuildStepHandlerActive)
		{
			return;
		}

		tree.ProcessFrame -= OnEditorRebuildStep;
		_editorRebuildStepHandlerActive = false;
	}

	private static void CancelPendingEditorRebuild()
	{
		var tree = _editorRebuildTree ?? _activeRebuildTree;
		DisconnectEditorProcessFrame(_editorRebuildTree);
		DisconnectEditorDelayedFrame(tree);
		DisconnectEditorRebuildStep(_activeRebuildTree);
		_editorRebuildTree = null;
		_activeRebuildTree = null;
		_editorRebuildStepPending = false;
		_editorRebuildAfterBulk = false;
		_editorRebuildPostBulkFramesRemaining = 0;
		_editorRebuildZeroMonsterRetries = 0;
		EditorRebuildSteps.Clear();
	}

	public static void RegisterOrUpdate(Monster monster)
	{
		if (!GodotObject.IsInstanceValid(monster) || !monster.IsInsideTree())
		{
			return;
		}

		InvalidateStaleState(monster);
		var monsterType = monster.MonsterType;
		if (RegisteredMonsterTypes.TryGetValue(monster, out var existingType) && existingType == monsterType)
		{
			UpdateTransform(monster);
			return;
		}

		Unregister(monster);
		var modelName = monster.GetVisualModelName();
		if (string.IsNullOrEmpty(modelName))
		{
			return;
		}

		var batch = GetOrCreateBatch(monster, monsterType, modelName);
		if (batch is null)
		{
			return;
		}

		batch.Add(monster);
		RegisteredMonsterTypes[monster] = monsterType;
	}

	private static void OnEditorProcessFrame()
	{
		if (_editorRebuildTree is null)
		{
			return;
		}

		var tree = _editorRebuildTree;
		DisconnectEditorProcessFrame(tree);
		_editorRebuildTree = tree;

		ConnectEditorDelayedFrame(tree);
	}

	private static void OnEditorProcessFrameDelayed()
	{
		if (_editorRebuildTree is null)
		{
			DisconnectEditorDelayedFrame(Engine.GetMainLoop() as SceneTree);
			return;
		}

		if (_editorRebuildAfterBulk && _editorRebuildPostBulkFramesRemaining > 0)
		{
			_editorRebuildPostBulkFramesRemaining--;
			return;
		}

		var tree = _editorRebuildTree;
		DisconnectEditorDelayedFrame(tree);
		_editorRebuildTree = null;
		ScheduleRebuildAllInEditedScene(tree);
	}

	private static void ScheduleRebuildAllInEditedScene(SceneTree tree)
	{
		var root = tree.EditedSceneRoot;
		if (root is null)
		{
			return;
		}

		if (CountMonstersInEditedScene(root) == 0
			&& _editorRebuildAfterBulk
			&& _editorRebuildZeroMonsterRetries < EditorRebuildZeroMonsterMaxRetries)
		{
			_editorRebuildZeroMonsterRetries++;
			_editorRebuildTree = tree;
			ConnectEditorDelayedFrame(tree);
			return;
		}

		_editorRebuildAfterBulk = false;
		_editorRebuildPostBulkFramesRemaining = 0;
		_editorRebuildZeroMonsterRetries = 0;

		EditorRebuildSteps.Clear();
		_activeRebuildTree = tree;
		EnqueueRebuildAllSteps(tree);
		ScheduleNextEditorRebuildStep();
	}

	private static int CountMonstersInEditedScene(Node root)
	{
		var count = 0;
		foreach (var node in root.FindChildren("*", recursive: true, owned: false))
		{
			if (node is Monster monster && GodotObject.IsInstanceValid(monster) && monster.IsInsideTree())
			{
				count++;
			}
		}

		return count;
	}

	private static void EnqueueRebuildAllSteps(SceneTree tree)
	{
		var root = tree.EditedSceneRoot;
		if (root is null)
		{
			return;
		}

		var monstersByType = new Dictionary<MonsterType, List<Monster>>();
		foreach (var node in root.FindChildren("*", recursive: true, owned: false))
		{
			if (node is not Monster monster || !GodotObject.IsInstanceValid(monster) || !monster.IsInsideTree())
			{
				continue;
			}

			if (!monstersByType.TryGetValue(monster.MonsterType, out var list))
			{
				list = [];
				monstersByType[monster.MonsterType] = list;
			}

			list.Add(monster);
		}

		Batches.Clear();
		RegisteredMonsterTypes.Clear();
		_visualRoot = null;

		foreach (var (monsterType, monsters) in monstersByType)
		{
			if (monsters.Count == 0)
			{
				continue;
			}

			var modelName = monsters[0].GetVisualModelName();
			if (string.IsNullOrEmpty(modelName))
			{
				continue;
			}

			var batch = GetOrCreateBatch(monsters[0], monsterType, modelName);
			if (batch is null)
			{
				continue;
			}

			batch.BeginSetMonsters(monsters.Count);
			for (var start = 0; start < monsters.Count; start += EditorRebuildChunkSize)
			{
				var chunkStart = start;
				var chunkCount = Math.Min(EditorRebuildChunkSize, monsters.Count - start);
				EditorRebuildSteps.Enqueue(() =>
				{
					if (!batch.IsValid)
					{
						return;
					}

					batch.SetMonsterChunk(monsters, chunkStart, chunkCount);
				});
			}

			EditorRebuildSteps.Enqueue(() =>
			{
				if (!batch.IsValid)
				{
					return;
				}

				batch.FinishSetMonsters(monsters);
				foreach (var monster in monsters)
				{
					if (GodotObject.IsInstanceValid(monster) && monster.IsInsideTree())
					{
						RegisteredMonsterTypes[monster] = monsterType;
					}
				}
			});
		}

		EditorRebuildSteps.Enqueue(CleanupOrphanTypeBatchNodes);
	}

	private static void ScheduleNextEditorRebuildStep()
	{
		if (_editorRebuildStepPending || _activeRebuildTree is null || EditorRebuildSteps.Count == 0)
		{
			return;
		}

		_editorRebuildStepPending = true;
		ConnectEditorRebuildStep(_activeRebuildTree);
	}

	private static void OnEditorRebuildStep()
	{
		_editorRebuildStepPending = false;
		if (_activeRebuildTree is null)
		{
			return;
		}

		if (EditorRebuildSteps.Count == 0)
		{
			DisconnectEditorRebuildStep(_activeRebuildTree);
			_activeRebuildTree = null;
			return;
		}

		try
		{
			EditorRebuildSteps.Dequeue().Invoke();
		}
		catch (Exception ex)
		{
			GD.PushError($"MonsterMultiMeshVisuals: editor rebuild step failed: {ex.Message}");
			EditorRebuildSteps.Clear();
			DisconnectEditorRebuildStep(_activeRebuildTree);
			_activeRebuildTree = null;
			return;
		}

		if (EditorRebuildSteps.Count == 0)
		{
			DisconnectEditorRebuildStep(_activeRebuildTree);
			_activeRebuildTree = null;
		}
	}

	private static void CleanupOrphanTypeBatchNodes()
	{
		if (_visualRoot is null || !GodotObject.IsInstanceValid(_visualRoot))
		{
			return;
		}

		var activeTypeRoots = new HashSet<ulong>();
		foreach (var batch in Batches.Values)
		{
			if (batch.IsValid)
			{
				activeTypeRoots.Add(batch.TypeRoot.GetInstanceId());
			}
		}

		foreach (var child in _visualRoot.GetChildren())
		{
			if (child is Node3D typeRoot && !activeTypeRoots.Contains(typeRoot.GetInstanceId()))
			{
				typeRoot.Free();
			}
		}
	}

	public static void UpdateTransformIfRegistered(Monster monster)
	{
		if (!RegisteredMonsterTypes.ContainsKey(monster))
		{
			return;
		}

		UpdateTransform(monster);
	}

	public static void Unregister(Monster monster)
	{
		if (IsBulkEditorUpdate)
		{
			return;
		}

		if (!RegisteredMonsterTypes.TryGetValue(monster, out var monsterType))
		{
			return;
		}

		RegisteredMonsterTypes.Remove(monster);
		if (Batches.TryGetValue(monsterType, out var batch))
		{
			batch.Remove(monster);
		}
	}

	private static void UpdateTransform(Monster monster)
	{
		if (!RegisteredMonsterTypes.TryGetValue(monster, out var monsterType))
		{
			return;
		}

		if (!Batches.TryGetValue(monsterType, out var batch))
		{
			return;
		}

		batch.UpdateTransform(monster);
	}

	private static MonsterTypeVisualBatch? GetOrCreateBatch(Monster monster, MonsterType monsterType, string modelName)
	{
		if (Batches.TryGetValue(monsterType, out var existing) && existing.IsValid)
		{
			return existing;
		}

		Batches.Remove(monsterType);

		var visualRoot = GetOrCreateVisualRoot(monster);
		if (visualRoot is null)
		{
			GD.PushWarning("MonsterMultiMeshVisuals: could not resolve visual root.");
			return null;
		}

		var meshParts = GetGroundedMeshParts(modelName);
		if (meshParts is null || meshParts.Count == 0)
		{
			GD.PushWarning($"MonsterMultiMeshVisuals: no mesh parts for model '{modelName}' ({monsterType}).");
			return null;
		}

		var typeRootName = $"MonsterMM_{monsterType}";
		Node3D typeRoot;
		if (visualRoot.GetNodeOrNull(typeRootName) is Node3D existingTypeRoot)
		{
			typeRoot = existingTypeRoot;
			ClearTypeRootChildren(typeRoot);
			SetTypeRootOwnerIfEditor(monster, typeRoot);
		}
		else
		{
			typeRoot = new Node3D { Name = typeRootName };
			visualRoot.AddChild(typeRoot);
			SetTypeRootOwnerIfEditor(monster, typeRoot);
		}

		var batch = new MonsterTypeVisualBatch(typeRoot, meshParts);
		Batches[monsterType] = batch;
		return batch;
	}

	/// <summary>
	///     Marks only the batch root owned so it appears in the scene tree. MultiMesh children stay unowned so
	///     instance buffers are not embedded into the .tscn on save.
	/// </summary>
	private static void SetTypeRootOwnerIfEditor(Monster context, Node typeRoot)
	{
		if (!Engine.IsEditorHint())
		{
			return;
		}

		var owner = context.GetTree()?.EditedSceneRoot ?? context.Owner;
		if (owner is not null)
		{
			typeRoot.Owner = owner;
		}
	}

	private static void ClearTypeRootChildren(Node3D typeRoot)
	{
		var children = typeRoot.GetChildren();
		foreach (var child in children)
		{
			child.Free();
		}
	}

	private static void InvalidateStaleState(Monster context)
	{
		var tree = context.GetTree();
		if (tree is null)
		{
			return;
		}

		var expectedParent = ResolveVisualRootParent(context, tree);
		if (_visualRoot is not null && GodotObject.IsInstanceValid(_visualRoot))
		{
			if (_visualRoot.GetParent() == expectedParent)
			{
				return;
			}
		}
		else if (_visualRoot is not null)
		{
			_visualRoot = null;
		}

		ClearAllBatches();
	}

	private static void ClearAllBatches()
	{
		Batches.Clear();
		RegisteredMonsterTypes.Clear();
	}

	private static Node? ResolveMainServerRoot(Monster context, SceneTree tree)
	{
		if (Engine.IsEditorHint())
		{
			return tree.EditedSceneRoot ?? context.Owner ?? context;
		}

		return tree.Root.GetNodeOrNull("MainServer") ?? tree.CurrentScene ?? tree.Root;
	}

	private static Node ResolveVisualRootParent(Monster context, SceneTree tree)
	{
		var mainServer = ResolveMainServerRoot(context, tree);
		if (mainServer is null)
		{
			return context;
		}

		if (mainServer.GetNodeOrNull(ObjectVisualsNodeName) is Node objectVisuals)
		{
			return objectVisuals;
		}

		var created = new Node3D { Name = ObjectVisualsNodeName };
		mainServer.AddChild(created);
		return created;
	}

	private static Node3D? GetOrCreateVisualRoot(Monster context)
	{
		var tree = context.GetTree();
		if (tree is null)
		{
			return null;
		}

		var parent = ResolveVisualRootParent(context, tree);
		if (_visualRoot is not null && GodotObject.IsInstanceValid(_visualRoot) && _visualRoot.GetParent() == parent)
		{
			return _visualRoot;
		}

		if (_visualRoot is not null && !GodotObject.IsInstanceValid(_visualRoot))
		{
			_visualRoot = null;
		}

		if (_visualRoot is not null && _visualRoot.GetParent() != parent)
		{
			ClearAllBatches();
			_visualRoot = null;
		}

		if (parent.GetNodeOrNull(VisualRootNodeName) is Node3D existing)
		{
			_visualRoot = existing;
			return _visualRoot;
		}

		_visualRoot = new Node3D { Name = VisualRootNodeName };
		parent.AddChild(_visualRoot);
		return _visualRoot;
	}

	private static List<GroundedMeshPart>? GetGroundedMeshParts(string modelName)
	{
		if (MeshPartsCache.TryGetValue(modelName, out var cached))
		{
			return cached;
		}

		var glbPath = $"{ModelsDirectory}{modelName}.glb";
		if (!ResourceLoader.Exists(glbPath))
		{
			MeshPartsCache[modelName] = null;
			return null;
		}

		var packed = ResourceLoader.Load<PackedScene>(glbPath);
		if (packed is null)
		{
			MeshPartsCache[modelName] = null;
			return null;
		}

		var parts = ExtractGroundedMeshParts(packed);
		MeshPartsCache[modelName] = parts;
		return parts;
	}

	private static List<GroundedMeshPart>? ExtractGroundedMeshParts(PackedScene scene)
	{
		var glbRoot = scene.Instantiate<Node3D>();
		try
		{
			ApplyGroundOffset(glbRoot);

			var parts = new List<GroundedMeshPart>();
			CollectMeshes(glbRoot, glbRoot, parts);
			return parts.Count == 0 ? null : parts;
		}
		finally
		{
			glbRoot.QueueFree();
		}
	}

	private static void ApplyGroundOffset(Node3D glbRoot)
	{
		var aabbFound = false;
		var combined = new Aabb();

		foreach (var child in glbRoot.FindChildren("*", "MeshInstance3D", recursive: true, owned: false))
		{
			if (child is not MeshInstance3D meshInstance || meshInstance.Mesh is not { } mesh)
			{
				continue;
			}

			var localAabb = meshInstance.GetAabb();
			if (localAabb.Size == Vector3.Zero)
			{
				continue;
			}

			var toGlbRoot = ComputeTransformRelativeToRoot(meshInstance, glbRoot);
			var transformed = TransformAabb(toGlbRoot, localAabb);
			combined = aabbFound ? combined.Merge(transformed) : transformed;
			aabbFound = true;
		}

		if (!aabbFound)
		{
			return;
		}

		var minY = combined.Position.Y;
		if (Mathf.Abs(minY) < 0.0001f)
		{
			return;
		}

		glbRoot.Position += new Vector3(0f, -minY, 0f);
	}

	private static void CollectMeshes(Node node, Node3D root, List<GroundedMeshPart> parts)
	{
		if (node is MeshInstance3D meshInstance && meshInstance.Mesh is { } mesh)
		{
			// Skinned GLBs (typical monsters) have no animation in MultiMesh; bind/rest pose is enough for placement.
			parts.Add(new GroundedMeshPart(mesh, ComputeTransformRelativeToRoot(meshInstance, root)));
		}

		foreach (var child in node.GetChildren())
		{
			CollectMeshes(child, root, parts);
		}
	}

	private static Transform3D ComputeTransformRelativeToRoot(Node3D node, Node3D root)
	{
		var transform = Transform3D.Identity;
		var current = node;
		while (!ReferenceEquals(current, root) && current is not null)
		{
			transform = current.Transform * transform;
			current = current.GetParent() as Node3D;
		}

		return transform;
	}

	private static Aabb TransformAabb(Transform3D transform, Aabb aabb)
	{
		var position = aabb.Position;
		var size = aabb.Size;
		var corners = new[]
		{
			new Vector3(position.X, position.Y, position.Z),
			new Vector3(position.X + size.X, position.Y, position.Z),
			new Vector3(position.X, position.Y + size.Y, position.Z),
			new Vector3(position.X, position.Y, position.Z + size.Z),
			new Vector3(position.X + size.X, position.Y + size.Y, position.Z),
			new Vector3(position.X + size.X, position.Y, position.Z + size.Z),
			new Vector3(position.X, position.Y + size.Y, position.Z + size.Z),
			new Vector3(position.X + size.X, position.Y + size.Y, position.Z + size.Z),
		};

		var first = transform * corners[0];
		var min = first;
		var max = first;
		for (var i = 1; i < corners.Length; i++)
		{
			var corner = transform * corners[i];
			min = new Vector3(Mathf.Min(min.X, corner.X), Mathf.Min(min.Y, corner.Y), Mathf.Min(min.Z, corner.Z));
			max = new Vector3(Mathf.Max(max.X, corner.X), Mathf.Max(max.Y, corner.Y), Mathf.Max(max.Z, corner.Z));
		}

		return new Aabb(min, max - min);
	}

	private sealed class GroundedMeshPart(Mesh mesh, Transform3D meshLocalToMonster)
	{
		public Mesh Mesh { get; } = mesh;
		public Transform3D MeshLocalToMonster { get; } = meshLocalToMonster;
	}

	private sealed class MonsterTypeVisualBatch
	{
		private readonly Node3D _typeRoot;
		private readonly List<MeshSlot> _meshSlots = [];
		private readonly List<Monster> _monsters = [];
		private readonly Dictionary<Monster, int> _monsterToIndex = new();

		public Node3D TypeRoot => _typeRoot;

		public bool IsValid => GodotObject.IsInstanceValid(_typeRoot) && _typeRoot.IsInsideTree();

		public MonsterTypeVisualBatch(Node3D typeRoot, List<GroundedMeshPart> meshParts)
		{
			_typeRoot = typeRoot;

			for (var i = 0; i < meshParts.Count; i++)
			{
				var part = meshParts[i];
				var multiMesh = new MultiMesh();
				multiMesh.Mesh = part.Mesh;
				multiMesh.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
				multiMesh.UseCustomData = false;

				var instance = new MultiMeshInstance3D
				{
					Name = meshParts.Count == 1 ? "Mesh" : $"Mesh_{i}",
					Multimesh = multiMesh,
					Visible = true,
				};
				typeRoot.AddChild(instance);
				_meshSlots.Add(new MeshSlot(multiMesh, instance, part.MeshLocalToMonster));
			}
		}

		public void Add(Monster monster)
		{
			var index = _monsters.Count;
			_monsters.Add(monster);
			_monsterToIndex[monster] = index;

			foreach (var slot in _meshSlots)
			{
				var count = index + 1;
				slot.MultiMesh.InstanceCount = count;
				slot.MultiMesh.SetInstanceTransform(index, BuildInstanceTransform(monster, slot.MeshLocalToMonster));
				slot.Instance.Multimesh = slot.MultiMesh;
			}
		}

		public void SetMonsters(IReadOnlyList<Monster> monsters)
		{
			BeginSetMonsters(monsters.Count);
			SetMonsterChunk(monsters, 0, monsters.Count);
			FinishSetMonsters(monsters);
		}

		public void BeginSetMonsters(int totalCount)
		{
			_monsters.Clear();
			_monsterToIndex.Clear();
			if (totalCount > 0)
			{
				_monsters.Capacity = Math.Max(_monsters.Capacity, totalCount);
			}

			foreach (var slot in _meshSlots)
			{
				slot.MultiMesh.InstanceCount = totalCount;
			}
		}

		public void SetMonsterChunk(IReadOnlyList<Monster> monsters, int startIndex, int count)
		{
			if (!IsValid)
			{
				return;
			}

			for (var i = 0; i < count; i++)
			{
				var index = startIndex + i;
				var monster = monsters[index];
				if (!GodotObject.IsInstanceValid(monster) || !monster.IsInsideTree())
				{
					continue;
				}

				if (index == _monsters.Count)
				{
					_monsters.Add(monster);
				}
				else if (index < _monsters.Count)
				{
					_monsters[index] = monster;
				}

				_monsterToIndex[monster] = index;

				foreach (var slot in _meshSlots)
				{
					slot.MultiMesh.SetInstanceTransform(index, BuildInstanceTransform(monster, slot.MeshLocalToMonster));
				}
			}
		}

		public void FinishSetMonsters(IReadOnlyList<Monster> monsters)
		{
			if (!IsValid)
			{
				return;
			}

			_monsters.Clear();
			_monsterToIndex.Clear();
			var writeIndex = 0;
			for (var i = 0; i < monsters.Count; i++)
			{
				var monster = monsters[i];
				if (!GodotObject.IsInstanceValid(monster) || !monster.IsInsideTree())
				{
					continue;
				}

				_monsters.Add(monster);
				_monsterToIndex[monster] = writeIndex;

				foreach (var slot in _meshSlots)
				{
					slot.MultiMesh.SetInstanceTransform(writeIndex, BuildInstanceTransform(monster, slot.MeshLocalToMonster));
				}

				writeIndex++;
			}

			foreach (var slot in _meshSlots)
			{
				slot.MultiMesh.InstanceCount = _monsters.Count;
				slot.Instance.Multimesh = slot.MultiMesh;
			}
		}

		public void ForgetMonster(Monster monster)
		{
			if (!_monsterToIndex.Remove(monster, out var index))
			{
				return;
			}

			var lastIndex = _monsters.Count - 1;
			if (index != lastIndex)
			{
				var lastMonster = _monsters[lastIndex];
				_monsters[index] = lastMonster;
				_monsterToIndex[lastMonster] = index;
			}

			_monsters.RemoveAt(lastIndex);
		}

		public void Remove(Monster monster)
		{
			if (!_monsterToIndex.TryGetValue(monster, out var index))
			{
				return;
			}

			var lastIndex = _monsters.Count - 1;
			if (index != lastIndex)
			{
				var lastMonster = _monsters[lastIndex];
				_monsters[index] = lastMonster;
				_monsterToIndex[lastMonster] = index;

				foreach (var slot in _meshSlots)
				{
					slot.MultiMesh.SetInstanceTransform(index, BuildInstanceTransform(lastMonster, slot.MeshLocalToMonster));
					slot.Instance.Multimesh = slot.MultiMesh;
				}
			}

			_monsters.RemoveAt(lastIndex);
			_monsterToIndex.Remove(monster);

			foreach (var slot in _meshSlots)
			{
				slot.MultiMesh.InstanceCount = _monsters.Count;
				slot.Instance.Multimesh = slot.MultiMesh;
			}
		}

		public void UpdateTransform(Monster monster)
		{
			if (!_monsterToIndex.TryGetValue(monster, out var index))
			{
				return;
			}

			foreach (var slot in _meshSlots)
			{
				slot.MultiMesh.SetInstanceTransform(index, BuildInstanceTransform(monster, slot.MeshLocalToMonster));
				slot.Instance.Multimesh = slot.MultiMesh;
			}
		}

		private Transform3D BuildInstanceTransform(Monster monster, Transform3D meshLocalToMonster)
		{
			if (!GodotObject.IsInstanceValid(monster) || !monster.IsInsideTree()
				|| !GodotObject.IsInstanceValid(_typeRoot) || !_typeRoot.IsInsideTree())
			{
				return Transform3D.Identity;
			}

			var monsterGlobal = monster.GlobalTransform;
			if (monsterGlobal.Basis.Determinant() == 0f)
			{
				monsterGlobal = ComputeGlobalTransformFromParents(monster);
			}

			return _typeRoot.GlobalTransform.AffineInverse() * monsterGlobal * meshLocalToMonster;
		}

		private static Transform3D ComputeGlobalTransformFromParents(Node3D node)
		{
			if (!GodotObject.IsInstanceValid(node))
			{
				return Transform3D.Identity;
			}

			var transform = node.Transform;
			var parent = node.GetParent() as Node3D;
			while (parent is not null && GodotObject.IsInstanceValid(parent))
			{
				transform = parent.Transform * transform;
				parent = parent.GetParent() as Node3D;
			}

			return transform;
		}

		private sealed class MeshSlot(MultiMesh multiMesh, MultiMeshInstance3D instance, Transform3D meshLocalToMonster)
		{
			public MultiMesh MultiMesh { get; } = multiMesh;
			public MultiMeshInstance3D Instance { get; } = instance;
			public Transform3D MeshLocalToMonster { get; } = meshLocalToMonster;
		}
	}
}
