namespace SphServer.Shared.WorldState;

/// <summary>
/// Container id for an item on the ground. The same 16 bytes are in the client
/// </summary>
public static class GroundContainer
{
    public static readonly Guid Id = new (new byte[]
    {
        0x7A, 0x1C, 0x4E, 0x90, 0xB3, 0x22, 0x4D, 0x18,
        0x91, 0xA0, 0x6F, 0x3C, 0x88, 0x15, 0x02, 0xD4
    });
}
