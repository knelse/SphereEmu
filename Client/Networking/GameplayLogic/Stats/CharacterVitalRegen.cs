using SphServer.Shared.Db.DataModels;

namespace SphServer.Client.Networking.GameplayLogic.Stats;

/// <summary>
///     Server-side mirror of <c>_player.Recalc</c> (CalcParamCli).
///     Rates and tick scale are MBC data defaults at 0x53DC..0x53E8 / g_51E0.
///     Caller owns the 6s cadence.
/// </summary>
public sealed class CharacterVitalRegen
{
    // _player.mbc data defaults
    private const float HpFlat = 0.33f;
    private const float HpPerMax = 0.0004f;
    private const float MpFlat = 0.55f;
    private const float MpPerMax = 5.6e-5f;
    private const int TickScale = 6; // g_51E0

    private float hpFrac;
    private float mpFrac;

    /// <summary>
    ///     One Recalc step (6s batch). No-op when dead. Returns true if HP or MP changed.
    /// </summary>
    public bool ApplyOnce(CharacterDbEntry character)
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
                var whole = (int)hpFrac;
                hpFrac -= whole;
                character.CurrentHP = (ushort)Math.Min(character.CurrentHP + whole, character.MaxHP);
            }
        }

        mpFrac += ((character.MaxMP * MpPerMax) + MpFlat) * TickScale;
        if (mpFrac >= 1f)
        {
            var whole = (int)mpFrac;
            mpFrac -= whole;
            character.CurrentMP = (ushort)Math.Min(character.CurrentMP + whole, character.MaxMP);
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
