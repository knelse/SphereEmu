using System;
using System.Linq;
using SphServer.Helpers;

namespace SphServer.Sphere.Game.NpcTrade.ItemsOnSale;

/// <summary>
/// No original vendor table survived, so the bands follow the monsters around each vendor: Hyperion
/// towns are the newbie area, the wild and Haron towns come next, then Phoebe, then Rodos.
/// Bands start at 1/4/7/10 like the Magic and Jewelry lists; tiers 13-15 are never sold
/// </summary>
public static class VendorTiers
{
    // Town vendors stand within ~300 of the center, the nearest wild ones from ~355
    private const double CityRadius = 320;

    public static (int Min, int Max) ForPosition (double x, double y, double z)
    {
        // Tavern and castle interiors sit above y 900 and have no gear vendors
        if (y > 900)
        {
            return (1, 3);
        }

        var continent = y switch
        {
            >= 250 => Continents.Харон,
            >= 50 => Continents.Гиперион,
            >= -250 => Continents.Феб,
            _ => Continents.Родос
        };

        var nearestCity = SavedCoords.TeleportPoints[continent][PoiType.CityCenter].Values
            .Min (c => Math.Sqrt ((c.x - x) * (c.x - x) + (c.z - z) * (c.z - z)));
        var band = Math.Min ((int) continent + (nearestCity <= CityRadius ? 0 : 1), 3);

        return (band * 3 + 1, band * 3 + 3);
    }
}
