using System.Collections.Generic;
using Godot;
using SphServer.Packets;
using SphServer.Shared.GameData.Enums;
using SphServer.Sphere.Game;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class Monster : WorldObject
{
	protected override bool AutoGroundGlbVisual => true;

	protected override bool RefreshModelVisualOnReady => true;

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

	private void RefreshMonsterInstanceFromType()
	{
		if (!MonsterTypeMapping.MonsterNameToMonsterTypeMapping.TryGetValue(_monsterType, out var monsterDbId))
		{
			return;
		}

		if (!GameObjectDb.Db.TryGetValue(monsterDbId, out var gameObject))
		{
			return;
		}

		MonsterInstance = new SphMonsterInstance(new SphMonsterData(gameObject), _level, _isNamed);
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
		var level = MonsterInstance.Level;
		MonsterInstance.MaxHp = level * data.HpPerLevel;
		MonsterInstance.CurrentHp = level * data.HpPerLevel;
		MonsterInstance.BasePAtk = level * data.PAtkPerLevel;
		MonsterInstance.BaseMAtk = level * data.MAtkPerLevel;
		MonsterInstance.BasePDef = level * data.PDefPerLevel;
		MonsterInstance.BaseMDef = level * data.MDefPerLevel;
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

		_level = instance.Level;
		_isNamed = instance.IsNamed;
		_currentHp = instance.CurrentHp;

		_maxHp = instance.MaxHp;
		_basePAtk = instance.BasePAtk;
		_baseMAtk = instance.BaseMAtk;
		_basePDef = instance.BasePDef;
		_baseMDef = instance.BaseMDef;
		_instanceKarmaType = instance.KarmaType;

		_dataGameId = data.GameId;
		_dataObjectType = data.ObjectType;
		_dataObjectKind = data.ObjectKind;
		_dataSphereType = data.SphereType;
		_dataModelNameGround = data.ModelNameGround;
		_dataModelNameInventory = data.ModelNameInventory;
		_dataKarmaType = data.KarmaType;
		_dataMinLevel = data.MinLevel;
		_dataMaxLevel = data.MaxLevel;
		_dataHpPerLevel = data.HpPerLevel;
		_dataPDefPerLevel = data.PDefPerLevel;
		_dataMDefPerLevel = data.MDefPerLevel;
		_dataPAtkPerLevel = data.PAtkPerLevel;
		_dataMAtkPerLevel = data.MAtkPerLevel;
		_dataMutatorId = data.MutatorId;
		_dataMutatorName = data.MutatorName;
		_dataSpeed = data.Speed;
		_dataRange = data.Range;
		_dataAttackDelay = data.AttackDelay;
	}

	private void NotifyInspector()
	{
		if (Engine.IsEditorHint())
		{
			NotifyPropertyListChanged();
		}
	}

	protected override string ResolveModelNameFromObjectTypeFallback()
	{
		if (TryGetMonsterModelNameGroundFromDb(MonsterType, out var ground))
		{
			return ground;
		}

		var dataGround = DataModelNameGround?.Trim() ?? string.Empty;
		if (!string.IsNullOrEmpty(dataGround))
		{
			return dataGround;
		}

		return base.ResolveModelNameFromObjectTypeFallback();
	}

	private MonsterType _monsterType;

	[Export]
	public MonsterType MonsterType
	{
		get => _monsterType;
		set
		{
			if (_monsterType == value)
			{
				return;
			}

			_monsterType = value;
			RefreshMonsterInstanceFromType();
			if (IsInsideTree())
			{
				CallDeferred(nameof(RefreshModelVisualDeferred));
			}
		}
	}

	[ExportGroup("Monster Instance")]
	private int _level = 1;

	[Export]
	public int Level
	{
		get => _level;
		set
		{
			if (_level == value)
			{
				return;
			}

			_level = value;
			if (MonsterInstance is not null)
			{
				MonsterInstance.Level = value;
				RecalculateMonsterInstanceStatsFromLevel();
				SyncExportedMonsterFields();
			}

			NotifyInspector();
		}
	}

	private bool _isNamed;

	[Export]
	public bool IsNamed
	{
		get => _isNamed;
		set
		{
			if (_isNamed == value)
			{
				return;
			}

			_isNamed = value;
			if (MonsterInstance is not null)
			{
				MonsterInstance.IsNamed = value;
			}

			NotifyInspector();
		}
	}

	private int _currentHp;

	[Export]
	public int CurrentHp
	{
		get => _currentHp;
		set
		{
			if (_currentHp == value)
			{
				return;
			}

			_currentHp = value;
			if (MonsterInstance is not null)
			{
				MonsterInstance.CurrentHp = value;
			}

			NotifyInspector();
		}
	}

	private int _maxHp;

	[Export]
	public int MaxHp
	{
		get => _maxHp;
		private set => _maxHp = value;
	}

	private int _basePAtk;

	[Export]
	public int BasePAtk
	{
		get => _basePAtk;
		private set => _basePAtk = value;
	}

	private int _baseMAtk;

	[Export]
	public int BaseMAtk
	{
		get => _baseMAtk;
		private set => _baseMAtk = value;
	}

	private int _basePDef;

	[Export]
	public int BasePDef
	{
		get => _basePDef;
		private set => _basePDef = value;
	}

	private int _baseMDef;

	[Export]
	public int BaseMDef
	{
		get => _baseMDef;
		private set => _baseMDef = value;
	}

	private KarmaTypes _instanceKarmaType;

	[Export]
	public KarmaTypes InstanceKarmaType
	{
		get => _instanceKarmaType;
		private set => _instanceKarmaType = value;
	}

	[ExportGroup("Monster Data")]
	private int _dataGameId;

	[Export]
	public int DataGameId
	{
		get => _dataGameId;
		private set => _dataGameId = value;
	}

	private GameObjectType _dataObjectType;

	[Export]
	public GameObjectType DataObjectType
	{
		get => _dataObjectType;
		private set => _dataObjectType = value;
	}

	private GameObjectKind _dataObjectKind;

	[Export]
	public GameObjectKind DataObjectKind
	{
		get => _dataObjectKind;
		private set => _dataObjectKind = value;
	}

	private string _dataSphereType = string.Empty;

	[Export]
	public string DataSphereType
	{
		get => _dataSphereType;
		private set => _dataSphereType = value;
	}

	private string _dataModelNameGround = string.Empty;

	[Export]
	public string DataModelNameGround
	{
		get => _dataModelNameGround;
		private set => _dataModelNameGround = value;
	}

	private string _dataModelNameInventory = string.Empty;

	[Export]
	public string DataModelNameInventory
	{
		get => _dataModelNameInventory;
		private set => _dataModelNameInventory = value;
	}

	private KarmaTypes _dataKarmaType;

	[Export]
	public KarmaTypes DataKarmaType
	{
		get => _dataKarmaType;
		private set => _dataKarmaType = value;
	}

	private int _dataMinLevel;

	[Export]
	public int DataMinLevel
	{
		get => _dataMinLevel;
		private set => _dataMinLevel = value;
	}

	private int _dataMaxLevel;

	[Export]
	public int DataMaxLevel
	{
		get => _dataMaxLevel;
		private set => _dataMaxLevel = value;
	}

	private int _dataHpPerLevel;

	[Export]
	public int DataHpPerLevel
	{
		get => _dataHpPerLevel;
		private set => _dataHpPerLevel = value;
	}

	private int _dataPDefPerLevel;

	[Export]
	public int DataPDefPerLevel
	{
		get => _dataPDefPerLevel;
		private set => _dataPDefPerLevel = value;
	}

	private int _dataMDefPerLevel;

	[Export]
	public int DataMDefPerLevel
	{
		get => _dataMDefPerLevel;
		private set => _dataMDefPerLevel = value;
	}

	private int _dataPAtkPerLevel;

	[Export]
	public int DataPAtkPerLevel
	{
		get => _dataPAtkPerLevel;
		private set => _dataPAtkPerLevel = value;
	}

	private int _dataMAtkPerLevel;

	[Export]
	public int DataMAtkPerLevel
	{
		get => _dataMAtkPerLevel;
		private set => _dataMAtkPerLevel = value;
	}

	private int _dataMutatorId;

	[Export]
	public int DataMutatorId
	{
		get => _dataMutatorId;
		private set => _dataMutatorId = value;
	}

	private string _dataMutatorName = string.Empty;

	[Export]
	public string DataMutatorName
	{
		get => _dataMutatorName;
		private set => _dataMutatorName = value;
	}

	private float _dataSpeed;

	[Export]
	public float DataSpeed
	{
		get => _dataSpeed;
		private set => _dataSpeed = value;
	}

	private int _dataRange;

	[Export]
	public int DataRange
	{
		get => _dataRange;
		private set => _dataRange = value;
	}

	private float _dataAttackDelay;

	[Export]
	public float DataAttackDelay
	{
		get => _dataAttackDelay;
		private set => _dataAttackDelay = value;
	}

	public override void _Ready()
	{
		// Server MultiMesh visuals must refresh when spawn placement sets GlobalPosition after AddChild.
		SetNotifyTransform(true);

		if (MonsterInstance is null)
		{
			RefreshMonsterInstanceFromType();
		}
		else
		{
			SyncExportedMonsterFields();
		}

		base._Ready();
	}

	public override void _ExitTree()
	{
		if (Engine.IsEditorHint() && MonsterMultiMeshVisuals.IsBulkEditorUpdate)
		{
			MonsterMultiMeshVisuals.ForgetMonster(this);
		}
		else
		{
			MonsterMultiMeshVisuals.Unregister(this);
		}

		base._ExitTree();
	}

	public override void _Notification(int what)
	{
		base._Notification(what);
		if (what == NotificationTransformChanged && !MonsterMultiMeshVisuals.IsBulkEditorUpdate)
		{
			MonsterMultiMeshVisuals.UpdateTransformIfRegistered(this);
		}
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

	[Export] public int NameID_1 { get; set; }
	[Export] public int NameID_2 { get; set; }
	[Export] public int NameID_3 { get; set; }
	public required SphMonsterInstance? MonsterInstance { get; set; }

	protected override List<PacketPart> GetPacketParts()
	{
		return (MonsterInstance?.Level ?? 1) - 1 == 0
			? PacketPart.LoadDefinedWithOverride("monster_level_1")
			: PacketPart.LoadDefinedWithOverride("monster_full");
	}

	protected override List<PacketPart> ModifyPacketParts(List<PacketPart> packetParts)
	{
		var hpSize = (MonsterInstance?.MaxHp ?? 50) >= 128 ? 16 : 8;
		PacketPart.UpdateValue(packetParts, "current_hp", MonsterInstance?.CurrentHp ?? 50, hpSize);
		PacketPart.UpdateValue(packetParts, "max_hp", MonsterInstance?.MaxHp ?? 50, hpSize);
		if (hpSize == 16)
		{
			PacketPart.UpdateValue(packetParts, "hp_size_t", 17, 5);
			PacketPart.UpdateValue(packetParts, "skip_1", 1, 1);
			PacketPart.UpdateValue(packetParts, "skip_2", 1, 1);
		}

		var objectType = MonsterInstance?.MonsterDataOrigin.ObjectType ?? GameObjectType.Monster;
		if (objectType is GameObjectType.Monster_Flying or GameObjectType.Monster_Event_Flying
			or GameObjectType.Special_Necromancer_Flyer)
		{
			PacketPart.UpdateValue(packetParts, "entity_type", (int)ObjectType.MonsterFlyer, 10);
		}

		var mobTypeId = MonsterTypeMapping.MonsterNameToMonsterTypeMapping[MonsterType];
		PacketPart.UpdateValue(packetParts, "mob_type", mobTypeId, 14);
		var levelToEncode = (MonsterInstance?.Level ?? 1) - 1;
		if (levelToEncode > 0)
		{
			PacketPart.UpdateValue(packetParts, "level_last_3", levelToEncode & 0b111, 3);
		}

		if (IsNamed)
		{
		}

		return packetParts;
	}
}
