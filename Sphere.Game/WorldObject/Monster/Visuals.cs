using Godot;
using SphServer.Helpers;
using SphServer.Shared.GameData.Enums;

namespace SphServer.Sphere.Game.WorldObject;

public partial class Monster
{
	protected override bool AutoGroundGlbVisual => true;

	protected override bool RefreshModelVisualOnReady => true;

	protected override bool SkipModelVisualRefreshOnEditorReady => true;

	private static bool TryGetMonsterModelNameGroundFromDb(MonsterType monsterType, out string modelName)
	{
		modelName = string.Empty;
		if (!MonsterTypeMapping.MonsterNameToMonsterTypeMapping.TryGetValue(monsterType, out var monsterDbId))
		{
			return false;
		}

		if (!SphObjectDb.GameObjectDataDb.TryGetValue(monsterDbId, out var entry))
		{
			return false;
		}

		var ground = entry.ModelNameGround?.Trim() ?? string.Empty;
		if (string.IsNullOrEmpty(ground))
		{
			return false;
		}

		modelName = ground;
		return true;
	}

	protected override string ResolveModelNameFromObjectTypeFallback()
	{
		if (TryGetMonsterModelNameGroundFromDb(MonsterType, out var ground))
		{
			return ground;
		}

		var dataGround = DataModelNameGround.Trim();
		if (!string.IsNullOrEmpty(dataGround))
		{
			return dataGround;
		}

		return base.ResolveModelNameFromObjectTypeFallback();
	}

	internal string GetVisualModelName() => GetEffectiveModelNameForVisual();

	protected override void RefreshModelVisual()
	{
		ClearLocalModelVisuals();
		if (Engine.IsEditorHint())
		{
			if (!MonsterMultiMeshVisuals.IsBulkEditorUpdate)
			{
				var tree = GetTree();
				if (tree is not null)
				{
					MonsterMultiMeshVisuals.RequestEditorRebuild(tree);
				}
			}

			return;
		}

		MonsterMultiMeshVisuals.RegisterOrUpdate(this);
	}

	public void RegisterMultiMeshVisualDeferred()
	{
		var tree = GetTree();
		if (tree is not null && Engine.IsEditorHint())
		{
			MonsterMultiMeshVisuals.RequestEditorRebuild(tree);
			return;
		}

		MonsterMultiMeshVisuals.RegisterOrUpdate(this);
	}

	public float GetSpawnOriginYOffset()
	{
		return GlbVisualGrounding.GetSpawnOriginYOffset(GetVisualModelName());
	}

	public float GetEditorVisualExtraYOffset()
	{
		return GlbVisualGrounding.GetEditorVisualExtraYOffset(GetVisualModelName());
	}
}
