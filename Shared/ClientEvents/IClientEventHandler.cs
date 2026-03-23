using System.Threading.Tasks;
using SphServer.Client;

namespace SphServer.Shared.ClientEvents;

public interface IClientEventHandler
{
    Task HandleAsync (ClientQueuedEvent clientEvent);
}
