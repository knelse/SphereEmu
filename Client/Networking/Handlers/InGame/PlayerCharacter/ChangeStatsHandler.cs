using System.Threading.Tasks;
using BitStreams;
using SphereHelpers.Extensions;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Shared.Logger;

namespace SphServer.Client.Networking.Handlers.InGame.PlayerCharacter;

public class ChangeStatsHandler(ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    private const int FirstDeltaBit = 141;
    private const int DeltaBits = 32;

    public async Task Handle(byte[] frame, double delta)
    {
        var character = clientConnection.GetSelectedCharacter();
        if (character is null)
        {
            return;
        }

        var stream = new BitStream(frame);
        stream.ReadBits(FirstDeltaBit);
        var strength = ReadClampedDelta(stream);
        var agility = ReadClampedDelta(stream);
        var accuracy = ReadClampedDelta(stream);
        var endurance = ReadClampedDelta(stream);
        var earth = ReadClampedDelta(stream);
        var air = ReadClampedDelta(stream);
        var water = ReadClampedDelta(stream);
        var fire = ReadClampedDelta(stream);

        if (!character.TrySpendStatPoints(strength, agility, accuracy, endurance, earth, air, water, fire))
        {
            SphLogger.Info(
                $"ChangeStats rejected STR+{strength} AGI+{agility} ACC+{accuracy} END+{endurance} " +
                $"EAR+{earth} AIR+{air} WAT+{water} FIR+{fire} " +
                $"(title {character.AvailableTitleStats}, degree {character.AvailableDegreeStats}). " +
                $"Client ID: {localId:X4}");
            return;
        }

        clientConnection.SaveSelectedCharacter();
        NetworkedStatsUpdater.Update(character);
        SphLogger.Info(
            $"ChangeStats STR+{strength} AGI+{agility} ACC+{accuracy} END+{endurance} " +
            $"EAR+{earth} AIR+{air} WAT+{water} FIR+{fire}. Client ID: {localId:X4}");
    }

    private static int ReadClampedDelta(BitStream stream)
    {
        var raw = (int)stream.ReadUInt32(DeltaBits);
        return raw < 0 ? 0 : raw;
    }
}
