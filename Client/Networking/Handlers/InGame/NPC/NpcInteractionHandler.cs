using System.Threading.Tasks;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Client.Networking.Handlers.InGame.NPC;

public class NpcInteractionHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (double delta)
    {
        clientConnection.DataStream.ReadBits(364);
        var vendorLocalId = clientConnection.DataStream.ReadUInt16();
        if (vendorLocalId == 0)
        {
            // first vendor open is 0x31, then client sends another 0x31 request to close the trade window,
            // and later it's 0x36 to open 0x31 to close. Sphere =/
            return;
        }

        // TODO: should use local to global id conversion
        var vendorWorldObject = ActiveWorldObjects.Get(vendorLocalId);
        if (vendorWorldObject is not NpcInteractable interactable)
        {
            SphLogger.Warning(
                $"Unable to interact with vendor [{vendorLocalId}]. Object not found. Client ID {localId:X4}");
            return;
        }

        interactable.ClientInteraction(localId, ClientInteractionType.OpenTrade);
    }
}