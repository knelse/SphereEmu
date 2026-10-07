using System.Collections.Generic;

namespace SphServer.Shared.GameData.Enums;

public enum DamageOriginSpecial
{
    Падение = 750,
    НехваткаВоздуха = 752,
    КоролевскийГнев = 755,
    ГневСкрижали = 756,
    Ожог = 768,
    Свечение = 770,
    Ловушка = 772,
    Турнир = 10365
}

public enum DeathOriginSpecial
{
    Падение = 751,
    НехваткаВоздуха = 753,
    КоролевскийГнев = 754,
    ГневСкрижали = 757,
    Ожог = 769,
    Ловушка = 773
}

public static class DamageOriginSpecialMapping
{
    public static readonly Dictionary<DamageOriginSpecial, DeathOriginSpecial> DeathByDamage = new ()
    {
        [DamageOriginSpecial.Падение] = DeathOriginSpecial.Падение,
        [DamageOriginSpecial.НехваткаВоздуха] = DeathOriginSpecial.НехваткаВоздуха,
        [DamageOriginSpecial.КоролевскийГнев] = DeathOriginSpecial.КоролевскийГнев,
        [DamageOriginSpecial.ГневСкрижали] = DeathOriginSpecial.ГневСкрижали,
        // hit_type 1 death default is gMsg 751
        [DamageOriginSpecial.Турнир] = DeathOriginSpecial.Падение,
        [DamageOriginSpecial.Ожог] = DeathOriginSpecial.Ожог,
        // hit_type 3 death is always gMsg 773
        [DamageOriginSpecial.Свечение] = DeathOriginSpecial.Ловушка,
        [DamageOriginSpecial.Ловушка] = DeathOriginSpecial.Ловушка
    };
}
