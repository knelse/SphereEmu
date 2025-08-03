using System;

namespace SphServer.System;

public class SphRng
{
    public static readonly Random Rng = new (Guid.NewGuid().GetHashCode());
}