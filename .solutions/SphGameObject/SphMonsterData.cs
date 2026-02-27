public class SphMonsterData (SphGameObject sphGameObject)
{
    public int GameId { get; set; } = sphGameObject.GameId;
    public GameObjectType ObjectType { get; } = sphGameObject.GameObjectType;
    public GameObjectKind ObjectKind { get; } = GameObjectKind.Monster;
    public string SphereType { get; set; } = sphGameObject.SphereType;
    public string ModelNameGround { get; set; } = sphGameObject.ModelNameGround;
    public string ModelNameInventory { get; set; } = sphGameObject.ModelNameInventory;
    public KarmaTypes KarmaType { get; set; } = (KarmaTypes) sphGameObject.TitleMinusOne;
    public int MinLevel { get; set; } = sphGameObject.DegreeMinusOne;
    public int MaxLevel { get; set; } = (int) sphGameObject.MinKarmaLevel; // shouldn't work, but does
    public int HpPerLevel { get; set; } = (int) sphGameObject.MaxKarmaLevel; // shouldn't work, but does
    public int PDefPerLevel { get; set; } = sphGameObject.StrengthReq;
    public int MDefPerLevel { get; set; } = sphGameObject.AgilityReq;
    public int PAtkPerLevel { get; set; } = sphGameObject.PAtkNegative;
    public int MAtkPerLevel { get; set; } = sphGameObject.MAtkNegativeOrHeal;
    public int MutatorId { get; set; } = sphGameObject.MutatorId;
    public string MutatorName { get; set; } = sphGameObject.TierRaw;
    public float Speed { get; set; } = 0; // TODO
    public int Range { get; set; } = sphGameObject.Range;
    public float AttackDelay { get; set; } = sphGameObject.UseTime;
}

public class SphMonsterInstance (SphMonsterData monsterData, int level, bool isNamed)
{
    public SphMonsterData MonsterDataOrigin { get; set; } = monsterData;
    public int Level { get; set; } = level; // explicit, not - 1
    public int MaxHp { get; set; } = level * monsterData.HpPerLevel;
    public int CurrentHp { get; set; } = level * monsterData.HpPerLevel;
    public int BasePAtk { get; set; } = level * monsterData.PAtkPerLevel;
    public int BaseMAtk { get; set; } = level * monsterData.MAtkPerLevel;
    public int BasePDef { get; set; } = level * monsterData.PDefPerLevel;
    public int BaseMDef { get; set; } = level * monsterData.MDefPerLevel;
    public bool IsNamed { get; set; } = isNamed;
    public KarmaTypes KarmaType = monsterData.KarmaType;
}