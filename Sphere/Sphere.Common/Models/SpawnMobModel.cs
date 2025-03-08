using Sphere.Common.Enums;
using Sphere.Common.Interfaces.GameObjects;
using Sphere.Common.Interfaces.Packets;
using Sphere.Common.Packets.Server;

namespace Sphere.Common.Models
{
    /// <summary>
    /// A model that contains necessary data to spawn a mob and get packet to be sent to the clients.
    /// </summary>
    public class SpawnMobModel : GameObject, ISpawnable
    {
        private const ushort HPSize = 8;
        public Coordinates Coordinates { get; set; }

        public int CurrentHP { get; set; }

        public int MaxHP { get; set; }

        public MonsterType Type { get; set; }

        public int Level { get; set; }

        public IServerPacketStream ToServerPacket()
        {
            return new SpawnMobPacket()
                .AddValue("entity_id", this.EntityId)
                .AddValue("entity_type", (int)GameObjectTypeEnum.Monster)
                .AddValue("action_type", (int)EntityActionTypeEnum.FULL_SPAWN)
                .AddValue(Coordinates)
                .AddValue("hp_size_t", HPSize)
                .AddValue("current_hp", CurrentHP)
                .AddValue("max_hp", MaxHP)
                .AddValue("mob_type", MonsterTypeMapping.MonsterNameToMonsterTypeMapping[Type])
                .AddValue("level", Level)
                .Build();            
        }
    }
}
