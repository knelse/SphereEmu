using System.Collections.Generic;

namespace SphServer.Sphere.Game;

public static class GameObjectDb
{
    public static readonly Dictionary<int, SphGameObject> Db = SphObjectDb.GameObjectDataDb;
}