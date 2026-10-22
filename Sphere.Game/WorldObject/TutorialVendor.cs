using SphServer.Client;

namespace SphServer.Sphere.Game.WorldObject;

public partial class TutorialVendor : NpcInteractable
{
    protected override bool RefreshModelVisualOnReady => false;

    // The dungeon replay declares this NPC; the node supplies its stock and trade handler
    protected override void ShowForClient (SphereClient client)
    {
    }
}
