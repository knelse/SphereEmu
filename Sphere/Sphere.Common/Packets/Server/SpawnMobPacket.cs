namespace Sphere.Common.Packets.Server
{
    public class SpawnMobPacket : ServerPacketBase
    {
        private static readonly ushort _size = 30;

        protected override string PacketName => "monster_level_1";
        
        protected override ushort Size => (ushort)(base.Size + _size + (this.Padding.Length / 8));
    }
}
