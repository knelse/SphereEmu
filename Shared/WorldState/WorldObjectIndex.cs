namespace SphServer.Shared.WorldState;

public static class WorldObjectIndex
{
    private static uint worldObjectIndex = 0x1000;

    public static uint GetCurrentIndex => worldObjectIndex;

    /// <summary>
    ///     Move the counter past ids that already exist. It lives in memory only, so without this a
    ///     restart begins at 0x1000 again and hands out ids that persisted rows already use.
    /// </summary>
    public static void SeedFrom (uint highestExistingId)
    {
        if (highestExistingId >= worldObjectIndex)
        {
            Interlocked.Exchange(ref worldObjectIndex, highestExistingId);
        }
    }

    public static ushort New ()
    {
        if (worldObjectIndex > 65535)
        {
            throw new ArgumentException("Reached max number of connections");
        }

        return (ushort) Interlocked.Increment(ref worldObjectIndex);
    }
}