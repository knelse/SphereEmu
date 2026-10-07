using System;
using Godot;
using SphServer.Helpers;
using SphServer.Server.Config;
using SphServer.Server.GameplayLogic.Combat;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.Logger;
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
			RefreshMonsterInstanceFromType ();
			ScheduleModelVisualRefreshIfNeeded ();
		}
	}

	[ExportGroup ("Monster Instance")]
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
				RecalculateMonsterInstanceStatsFromLevel ();
				SyncExportedMonsterFields ();
			}

			NotifyInspector ();
		}
	}

	private NamedBossRank namedBossRank = NamedBossRank.None;

	/// <summary>
	/// Any rank but None rolls a syllable name and applies that rank's coefficient from combat.json
	/// </summary>
	[Export]
	public NamedBossRank NamedBossRank
	{
		get => namedBossRank;
		set
		{
			if (namedBossRank == value)
			{
				return;
			}

			namedBossRank = value;
			if (value == NamedBossRank.None)
			{
				nameCode = -1;
			}
			else if (nameCode == -1)
			{
				nameCode = MonsterNameCode.Roll (out _);
			}

			if (MonsterInstance is not null)
			{
				MonsterInstance.NamedBossRank = value;
				RecalculateMonsterInstanceStatsFromLevel ();
				SyncExportedMonsterFields ();
			}

			NotifyInspector ();
		}
	}

	private int nameCode = -1;

	/// <summary>
	/// Packed MNS1/MNS2/MNS3 syllable code (TradeMan cmd 3); -1 omits the region
	/// </summary>
	[Export]
	public int NameCode
	{
		get => nameCode;
		set => nameCode = value;
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

			NotifyInspector ();
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
	public int CurrentMaxHp
	{
		get => MaxHp;
		private set => MaxHp = value;
	}

	[Export]
	public int CurrentPAtk
	{
		get => BasePAtk;
		private set => BasePAtk = value;
	}

	[Export]
	public int CurrentMAtk
	{
		get => BaseMAtk;
		private set => BaseMAtk = value;
	}

	[Export]
	public int CurrentPDef
	{
		get => BasePDef;
		private set => BasePDef = value;
	}

	[Export]
	public int CurrentMDef
	{
		get => BaseMDef;
		private set => BaseMDef = value;
	}

	[Export]
	public KarmaTypes InstanceKarmaType { get; private set; }

	[field: ExportGroup ("Monster Data")]
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
	public float DataWalkSpeed { get; private set; }

	[Export]
	public float DataRunSpeed { get; private set; }

	[Export]
	public int DataRange { get; private set; }

	[Export]
	public float DataAttackDelay { get; private set; }

	private void RefreshMonsterInstanceFromType ()
	{
		if (!MonsterTypeMapping.MonsterNameToMonsterTypeMapping.TryGetValue (monsterType, out var monsterDbId))
		{
			return;
		}

		if (!GameObjectDb.Db.TryGetValue (monsterDbId, out var gameObject))
		{
			return;
		}

		MonsterInstance = new SphMonsterInstance (new SphMonsterData (gameObject), level, namedBossRank);
		RecalculateMonsterInstanceStatsFromLevel ();
		SyncExportedMonsterFields ();
		NotifyInspector ();
	}

	private void RecalculateMonsterInstanceStatsFromLevel ()
	{
		if (MonsterInstance is null)
		{
			return;
		}

		var data = MonsterInstance.MonsterDataOrigin;
		var mobLevel = MonsterInstance.Level;
		var coefficient = NamedBossStatCoefficient (MonsterInstance.NamedBossRank);
		MonsterInstance.MaxHp = ApplyNamedBossCoefficient (mobLevel * data.HpPerLevel, coefficient);
		MonsterInstance.CurrentHp = MonsterInstance.MaxHp;
		MonsterInstance.BasePAtk = ApplyNamedBossCoefficient (mobLevel * data.PAtkPerLevel, coefficient);
		MonsterInstance.BaseMAtk = ApplyNamedBossCoefficient (mobLevel * data.MAtkPerLevel, coefficient);
		MonsterInstance.BasePDef = ApplyNamedBossCoefficient (mobLevel * data.PDefPerLevel, coefficient);
		MonsterInstance.BaseMDef = ApplyNamedBossCoefficient (mobLevel * data.MDefPerLevel, coefficient);
		MonsterInstance.KarmaType = data.KarmaType;
	}

	private static double NamedBossStatCoefficient (NamedBossRank rank)
	{
		if (Engine.IsEditorHint ())
		{
			return 1d;
		}

		var combat = BalanceConfig.Get<CombatBalance> ("combat");
		if (combat is null)
		{
			SphLogger.Error (
				$"Named boss stat coefficient unavailable for {rank}; leaving hp and combat stats unbuffed.");
			return 1d;
		}

		return combat.StatMultiplier (rank);
	}

	private static int ApplyNamedBossCoefficient (int value, double coefficient)
	{
		if (coefficient == 1d)
		{
			return value;
		}

		var scaled = Math.Round (value * coefficient, MidpointRounding.AwayFromZero);
		if (scaled >= int.MaxValue)
		{
			return int.MaxValue;
		}

		if (scaled <= int.MinValue)
		{
			return int.MinValue;
		}

		return (int) scaled;
	}

	private void SyncExportedMonsterFields ()
	{
		if (MonsterInstance is null)
		{
			return;
		}

		var instance = MonsterInstance;
		var data = instance.MonsterDataOrigin;

		level = instance.Level;
		namedBossRank = instance.NamedBossRank;
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
		DataWalkSpeed = data.WalkSpeed;
		DataRunSpeed = data.RunSpeed;
		DataRange = data.Range;
		DataAttackDelay = data.AttackDelay;
	}

	private void NotifyInspector ()
	{
		if (Engine.IsEditorHint ())
		{
			NotifyPropertyListChanged ();
		}
	}
}
