using Godot;
using SphServer.Helpers;
using SphServer.Shared.GameData.Enums;
using SphServer.Sphere.Game;

namespace SphServer.Sphere.Game.WorldObject;

public partial class Monster
{
	private MonsterType monsterType;

	[Export]
	public MonsterType MonsterType
	{
		get => monsterType;
		set
		{
			if (monsterType == value)
			{
				return;
			}

			monsterType = value;
			RefreshMonsterInstanceFromType();
			ScheduleModelVisualRefreshIfNeeded();
		}
	}

	[ExportGroup("Monster Instance")]
	private int level = 1;

	[Export]
	public int Level
	{
		get => level;
		set
		{
			if (level == value)
			{
				return;
			}

			level = value;
			if (MonsterInstance is not null)
			{
				MonsterInstance.Level = value;
				RecalculateMonsterInstanceStatsFromLevel();
				SyncExportedMonsterFields();
			}

			NotifyInspector();
		}
	}

	private bool isNamed;

	[Export]
	public bool IsNamed
	{
		get => isNamed;
		set
		{
			if (isNamed == value)
			{
				return;
			}

			isNamed = value;
			if (MonsterInstance is not null)
			{
				MonsterInstance.IsNamed = value;
			}

			NotifyInspector();
		}
	}

	private int currentHp;

	[Export]
	public int CurrentHp
	{
		get => currentHp;
		set
		{
			if (currentHp == value)
			{
				return;
			}

			currentHp = value;
			if (MonsterInstance is not null)
			{
				MonsterInstance.CurrentHp = value;
			}

			NotifyInspector();
		}
	}

	[Export]
	public int MaxHp { get; private set; }

	[Export]
	public int BasePAtk { get; private set; }

	[Export]
	public int BaseMAtk { get; private set; }

	[Export]
	public int BasePDef { get; private set; }

	[Export]
	public int BaseMDef { get; private set; }

	[Export]
	public KarmaTypes InstanceKarmaType { get; private set; }

	[field: ExportGroup("Monster Data")]
	[Export]
	public int DataGameId { get; private set; }

	[Export]
	public GameObjectType DataObjectType { get; private set; }

	[Export]
	public GameObjectKind DataObjectKind { get; private set; }

	[Export]
	public string DataSphereType { get; private set; } = string.Empty;

	[Export]
	public string DataModelNameGround { get; private set; } = string.Empty;

	[Export]
	public string DataModelNameInventory { get; private set; } = string.Empty;

	[Export]
	public KarmaTypes DataKarmaType { get; private set; }

	[Export]
	public int DataMinLevel { get; private set; }

	[Export]
	public int DataMaxLevel { get; private set; }

	[Export]
	public int DataHpPerLevel { get; private set; }

	[Export]
	public int DataPDefPerLevel { get; private set; }

	[Export]
	public int DataMDefPerLevel { get; private set; }

	[Export]
	public int DataPAtkPerLevel { get; private set; }

	[Export]
	public int DataMAtkPerLevel { get; private set; }

	[Export]
	public int DataMutatorId { get; private set; }

	[Export]
	public string DataMutatorName { get; private set; } = string.Empty;

	[Export]
	public float DataSpeed { get; private set; }

	[Export]
	public int DataRange { get; private set; }

	[Export]
	public float DataAttackDelay { get; private set; }

	private void RefreshMonsterInstanceFromType()
	{
		if (!MonsterTypeMapping.MonsterNameToMonsterTypeMapping.TryGetValue(monsterType, out var monsterDbId))
		{
			return;
		}

		if (!GameObjectDb.Db.TryGetValue(monsterDbId, out var gameObject))
		{
			return;
		}

		MonsterInstance = new SphMonsterInstance(new SphMonsterData(gameObject), level, isNamed);
		SyncExportedMonsterFields();
		NotifyInspector();
	}

	private void RecalculateMonsterInstanceStatsFromLevel()
	{
		if (MonsterInstance is null)
		{
			return;
		}

		var data = MonsterInstance.MonsterDataOrigin;
		var mobLevel = MonsterInstance.Level;
		MonsterInstance.MaxHp = mobLevel * data.HpPerLevel;
		MonsterInstance.CurrentHp = mobLevel * data.HpPerLevel;
		MonsterInstance.BasePAtk = mobLevel * data.PAtkPerLevel;
		MonsterInstance.BaseMAtk = mobLevel * data.MAtkPerLevel;
		MonsterInstance.BasePDef = mobLevel * data.PDefPerLevel;
		MonsterInstance.BaseMDef = mobLevel * data.MDefPerLevel;
		MonsterInstance.KarmaType = data.KarmaType;
	}

	private void SyncExportedMonsterFields()
	{
		if (MonsterInstance is null)
		{
			return;
		}

		var instance = MonsterInstance;
		var data = instance.MonsterDataOrigin;

		level = instance.Level;
		isNamed = instance.IsNamed;
		currentHp = instance.CurrentHp;

		MaxHp = instance.MaxHp;
		BasePAtk = instance.BasePAtk;
		BaseMAtk = instance.BaseMAtk;
		BasePDef = instance.BasePDef;
		BaseMDef = instance.BaseMDef;
		InstanceKarmaType = instance.KarmaType;

		DataGameId = data.GameId;
		DataObjectType = data.ObjectType;
		DataObjectKind = data.ObjectKind;
		DataSphereType = data.SphereType;
		DataModelNameGround = data.ModelNameGround;
		DataModelNameInventory = data.ModelNameInventory;
		DataKarmaType = data.KarmaType;
		DataMinLevel = data.MinLevel;
		DataMaxLevel = data.MaxLevel;
		DataHpPerLevel = data.HpPerLevel;
		DataPDefPerLevel = data.PDefPerLevel;
		DataMDefPerLevel = data.MDefPerLevel;
		DataPAtkPerLevel = data.PAtkPerLevel;
		DataMAtkPerLevel = data.MAtkPerLevel;
		DataMutatorId = data.MutatorId;
		DataMutatorName = data.MutatorName;
		DataSpeed = data.Speed;
		DataRange = data.Range;
		DataAttackDelay = data.AttackDelay;
	}

	private void NotifyInspector()
	{
		if (Engine.IsEditorHint())
		{
			NotifyPropertyListChanged();
		}
	}
}
