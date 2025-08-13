using System.Threading.Tasks;
using SphServer.Helpers;

namespace SphServer.Client.Networking.Handlers.BeforeGame;

public class NewPlayerDungeonHandler
{
    // TDOO: implement
    /*
     *             case ClientState.INIT_WAITING_FOR_CLIENT_INGAME_ACK:
                // currentState = ClientState.INIT_NEW_DUNGEON_TELEPORT_DELAY;
                // await ToSignal(GetTree().CreateTimer(3), "timeout");
                // currentState = ClientState.INIT_NEW_DUNGEON_TELEPORT_READY_TO_INIT;
                currentState = ClientState.INGAME_DEFAULT;
                break;
            case ClientState.INIT_NEW_DUNGEON_TELEPORT_DELAY:
                return;
            case ClientState.INIT_NEW_DUNGEON_TELEPORT_READY_TO_INIT:
                await MoveToNewPlayerDungeonAsync(CurrentCharacter);
                // StreamPeer.PutData(CurrentCharacter.GetTeleportByteArray(new WorldCoords(2584, 160, 1426, -1.5)));
                break;
     */

    public async Task Teleport (double delta)
    {
        var newDungeonCoords = new WorldCoords(-1098, -4501.62158203125, 1900);
        var playerCoords = new WorldCoords(-1098.69506835937500, -4501.61474609375000, 1900.05493164062500,
            1.57079637050629);
        // var playerCoords = new WorldCoords(0, 150, 0);
        // clientConnection.MaybeQueueNetworkPacketSend(CurrentCharacter.GetTeleportByteArray(playerCoords));
        // clientConnection.MaybeQueueNetworkPacketSend(selectedCharacter.GetNewPlayerDungeonTeleportAndUpdateStatsByteArray(playerCoords));
        // here some stats are updated because satiety gets applied. We'll figure that out later, for now just flat

        // currentState = ClientState.INIT_NEW_DUNGEON_TELEPORT_INITIATED;
        // await ToSignal(GetTree().CreateTimer(0.5f), "timeout");

        // clientConnection.MaybeQueueNetworkPacketSend(CommonPackets.LoadNewPlayerDungeon);
        // ConsoleHelper.WriteLine(
        // $"SRV: Teleported client [{MinorByte(selectedCharacter.ClientIndex) * 256 + MajorByte(selectedCharacter.ClientIndex)}] to default new player dungeon");

        // currentState = ClientState.INGAME_DEFAULT;
        // var clientTransform = clientModel!.Transform;
        // clientTransform.Origin.X = (float) playerCoords.x;
        // clientTransform.Origin.Y = (float) playerCoords.y;
        // clientTransform.Origin.Z = (float) playerCoords.z;
        // clientModel.Transform = clientTransform;
        //
        // CurrentCharacter.MaxHP = 110;
        // CurrentCharacter.MaxSatiety = 100;
        // CurrentCharacter.Money = 10;
        // CurrentCharacter.UpdateCurrentStats();
    }
}