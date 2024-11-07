using Sphere.Common.Packets;

namespace Sphere.Common.Entities
{
    public class PlayerEntity : BaseEntity
    {
        public string Login { get; set; }

        public string Password { get; set; }

        public byte[] ToInitialDataByteArray(ushort clientId)
        {
            var data = new List<byte>();
            
            for (var i = 0; i < 3; i++)
            {
                var characterData = CommonPackets.CreateNewCharacterData(clientId);

                data.AddRange(characterData);
            }

            return data.ToArray();
        }
    }
}
