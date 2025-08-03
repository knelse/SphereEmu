using System;
using System.Threading;

namespace SphServer.Shared.WorldState;

public static class WorldObjectIndex
{
    private static uint worldObjectIndex = 0x1000;

    public static ushort New ()
    {
        if (worldObjectIndex > 65535)
        {
            throw new ArgumentException("Reached max number of connections");
        }

        return (ushort) Interlocked.Increment(ref worldObjectIndex);
    }
}