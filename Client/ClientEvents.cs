using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SphServer.Client.EventHandlers;

namespace SphServer.Client;

public class ClientEvents (SphereClient sphereClient)
{
    private readonly Queue<ClientQueuedEvent> eventQueue = new ();
    private CurrentClientPositionChangedEventHandler? currentClientPositionChangedEventHandler;
    private EntityPositionUpdateEventHandler? entityPositionUpdateEventHandler;

    public void Enqueue (ClientQueuedEvent clientEvent)
    {
        eventQueue.Enqueue (clientEvent);
    }

    public void InitEventHandlers ()
    {
        currentClientPositionChangedEventHandler ??= new CurrentClientPositionChangedEventHandler (sphereClient);
        entityPositionUpdateEventHandler ??= new EntityPositionUpdateEventHandler (sphereClient);
    }

    public async Task HandleEventsAsync ()
    {
        InitEventHandlers ();
        while (eventQueue.Count > 0)
        {
            await DispatchAsync (eventQueue.Dequeue ()).ConfigureAwait (false);
        }
    }

    private async Task DispatchAsync (ClientQueuedEvent clientEvent)
    {
        switch (clientEvent)
        {
            case CurrentClientPositionChangedEvent e:
                await currentClientPositionChangedEventHandler!.HandleAsync (e).ConfigureAwait (false);
                break;
            case EntityPositionUpdateEvent e:
                await entityPositionUpdateEventHandler!.HandleAsync (e).ConfigureAwait (false);
                break;
            default:
                throw new ArgumentOutOfRangeException (
                    nameof (clientEvent),
                    $"Unhandled client event type: {clientEvent.GetType ().Name}.");
        }
    }
}
