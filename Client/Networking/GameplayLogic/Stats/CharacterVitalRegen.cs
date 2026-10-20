using SphServer.Shared.Db.DataModels;

namespace SphServer.Client.Networking.GameplayLogic.Stats;

/// <summary>
/// Mirror of _player.Recalc; rates are the MBC defaults at 0x53DC..0x53E8 / g_51E0
/// </summary>
public sealed class CharacterVitalRegen
{
    // _player.mbc data defaults
    private const float HpFlat = 0.33f;
    private const float HpPerMax = 0.0004f;
    private const float MpFlat = 0.55f;
    private const float MpPerMax = 5.6e-5f;
    private const int TickScale = 6; // g_51E0

    /// <summary>
    /// The client never lowers satiety itself (_player.DHunger has no caller), so the server does.
    /// No original rate survived: one point every 20 ticks, 2 minutes, empties the usual 100 in
    /// about 3.3 hours online
    /// </summary>
    private const int TicksPerSatietyPoint = 20;

    private float hpFrac;
    private float mpFrac;
    private int hungerTicks;

    /// <summary>
    /// One 6s step of hunger; true when satiety went down. A corpse does not get hungry
    /// </summary>
    public bool ApplyHunger (CharacterDbEntry character)
    {
        if (character.CurrentHP <= 0 || character.CurrentSatiety == 0)
        {
            return false;
        }

        if (++hungerTicks < TicksPerSatietyPoint)
        {
            return false;
        }

        hungerTicks = 0;
        character.CurrentSatiety--;
        return true;
    }

    /// <summary>
    /// Dead is a no-op, so a 6s step does not regen a corpse
    /// </summary>
    public bool ApplyOnce (CharacterDbEntry character)
    {
        if (character.CurrentHP <= 0)
        {
            // Drop pending fractional regen so a later respawn does not jump ahead of server.
            hpFrac = 0f;
            return false;
        }

        var hpBefore = character.CurrentHP;
        var mpBefore = character.CurrentMP;

        if (character.CurrentSatiety > 0)
        {
            var satietyFactor = 0.3f + character.CurrentSatiety / 114f;
            hpFrac += ((character.MaxHP * HpPerMax) + HpFlat) * TickScale * satietyFactor;
            if (hpFrac >= 1f)
            {
                var whole = (int) hpFrac;
                hpFrac -= whole;
                character.CurrentHP = (ushort) Math.Min (character.CurrentHP + whole, character.MaxHP);
            }
        }

        mpFrac += ((character.MaxMP * MpPerMax) + MpFlat) * TickScale;
        if (mpFrac >= 1f)
        {
            var whole = (int) mpFrac;
            mpFrac -= whole;
            character.CurrentMP = (ushort) Math.Min (character.CurrentMP + whole, character.MaxMP);
        }

        if (character.CurrentHP > character.MaxHP)
        {
            character.CurrentHP = character.MaxHP;
        }

        if (character.CurrentMP > character.MaxMP)
        {
            character.CurrentMP = character.MaxMP;
        }

        return character.CurrentHP != hpBefore || character.CurrentMP != mpBefore;
    }
}
