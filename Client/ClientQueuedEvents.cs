namespace SphServer.Client;

public abstract record ClientQueuedEvent;

public sealed record CurrentClientPositionChangedEvent : ClientQueuedEvent;

public sealed record EntityPositionUpdateEvent (ushort EntityId, double X, double Y, double Z, double Angle)
    : ClientQueuedEvent;
