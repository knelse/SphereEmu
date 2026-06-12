using Godot;
using SphServer.Godot.Scripts.Objects;
using SphServer.Helpers;
using SphServer.Packets;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class CastleEntrance : WorldObject
{
    public CastleEntrance()
    {
        ObjectType = ObjectType.CastleEntrance;
        ModelName = "edoor";
    }

    [Export] public Castles Castle { get; set; }

    [ExportToolButton("Jump to tablet")] public Callable JumpToTabletButton => Callable.From(JumpToTablet);

    protected override List<PacketPart> ModifyPacketParts(List<PacketPart> packetParts)
    {
        PacketPart.UpdateValue(packetParts, "object_type", (int)ObjectType, 10);
        PacketPart.UpdateValue(packetParts, "castle_id", (int)(Castle + 56), 7);

        return packetParts;
    }

    private void JumpToTablet()
    {
        if (!Engine.IsEditorHint())
        {
            return;
        }

        EditorSceneCamera.JumpToCastleTablet(this, Castle);
    }
}