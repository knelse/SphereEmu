using System.Threading.Tasks;

namespace SphServer.Client.Networking.Handlers.InGame.Communities;

public class ClanActionsHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (double delta)
    {
        //     // clan
        //     var clientLocalId = (ushort) ((rcvBuffer[11] << 8) + rcvBuffer[12]);
        //     // there might be a better way to select action
        //     var shouldCreate = rcvBuffer[18] == 0x00 && rcvBuffer[19] == 0x00 && rcvBuffer[20] == 0x00;
        //     if (shouldCreate)
        //     {
        //         var nameLength = (rcvBuffer[16] >> 5) + ((rcvBuffer[17] & 0b11111) << 3) - 5;
        //         var name = new List<byte>();
        //
        //         for (var i = 0; i < nameLength; i++)
        //         {
        //             name.Add((byte) ((rcvBuffer[i + 21] >> 5) + ((rcvBuffer[i + 22] & 0b11111) << 3)));
        //         }
        //
        //         var clanNameBytes = name.ToArray();
        //         var clanNameString = MainServer.Win1251.GetString(clanNameBytes);
        //
        //         Console.WriteLine($"[{clientLocalId:X}] Create clan [{clanNameString}]");
        //
        //         var characterNameBytes = MainServer.Win1251.GetBytes(CurrentCharacter.Name);
        //
        //         // change clan rank
        //         var responseStream = GetWriteBitStream();
        //         responseStream.WriteBytes(new byte[]
        //         {
        //             (byte) (characterNameBytes.Length + 27), 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00,
        //             MajorByte(clientLocalId), MinorByte(clientLocalId), 0x08, 0x40, 0xC3, 0x22, 0x20, 0xA0, 0x71
        //         }, 16, true);
        //         responseStream.WriteByte(0x1, 4);
        //         responseStream.WriteByte((byte) (characterNameBytes.Length + 5));
        //         responseStream.WriteByte(0x1);
        //         responseStream.WriteByte(0x0);
        //         responseStream.WriteByte(MajorByte(clientLocalId));
        //         responseStream.WriteByte(MinorByte(clientLocalId));
        //         responseStream.WriteByte(0x0);
        //         responseStream.WriteBytes(characterNameBytes, characterNameBytes.Length, true);
        //         responseStream.WriteByte(0x0D);
        //         responseStream.WriteByte(0x12);
        //         responseStream.WriteByte(0x2);
        //         responseStream.WriteByte(0xA, 4);
        //         responseStream.WriteByte(0x0);
        //         var response = responseStream.GetStreamData();
        //         StreamPeer.PutData(response);
        //
        //         CurrentCharacter.Clan = new Clan
        //         {
        //             Id = 999,
        //             Name = clanNameString
        //         };
    }
}