using System.Threading.Tasks;
using BitStreams;
using SphereHelpers.Extensions;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Client.Networking.Handlers.InGame.NPC;

public class NpcInteractionHandler (Guid entityId)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (byte[] frame, double delta)
    {
        var stream = new BitStream (frame);
        stream.ReadBits (474);
        var vendorId = stream.ReadGuid ();
        if (vendorId == Guid.Empty)
        {
            // first vendor open is 0x31, then client sends another 0x31 request to close the trade
            // window,
            // and later it's 0x36 to open 0x31 to close. Sphere =/
            return;
        }

        var vendorWorldObject = ActiveWorldObjects.Get (vendorId);
        if (vendorWorldObject is not NpcInteractable interactable)
        {
            SphLogger.Warning (
                $"Unable to interact with vendor [{vendorId}]. Object not found. Client ID {entityId}");
            return;
        }

        interactable.ClientInteraction (entityId, ClientInteractionType.OpenTrade);
    }
}
