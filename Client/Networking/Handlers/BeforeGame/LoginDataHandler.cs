using System;
using System.Threading.Tasks;
using Godot;
using SphServer.Server.Config;
using SphServer.Server.Login.Auth;
using SphServer.Server.Login.Decoders;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.Networking.DataModel.Serializers;
using SphServer.Shared.WorldState;
using SphServer.System;

namespace SphServer.Client.Networking.Handlers.BeforeGame;

public class LoginDataHandler (StreamPeerTcp streamPeerTcp, ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    private SphereTimer? WaitForClientTimer;

    public async Task Handle (double delta)
    {
        if (WaitForClientTimer is not null)
        {
            WaitForClientTimer.Tick(delta);
            return;
        }

        var incomingDataLength = clientConnection.GetIncomingData();

        if (incomingDataLength <= 12)
        {
            return;
        }

        SphLogger.Info($"CLI {localId:X4}: Login data sent");
        var (login, password) = LoginDecoder.DecodeFromBuffer(clientConnection.ReceiveBuffer[..incomingDataLength]);
        if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
        {
            SphLogger.Error($"SRV {localId:X4}: Invalid login.");
            streamPeerTcp.PutData(CommonPackets.CannotConnect(localId));
            clientConnection.Close();
            return;
        }

        if (BannedClients.IsLoginBanned(login))
        {
            SphLogger.Error($"SRV {localId:X4}: Login is banned. Login: {login}");
            streamPeerTcp.PutData(CommonPackets.CannotConnect(localId));
            clientConnection.Close();
            return;
        }

        var player =
            LoginManager.CheckLoginAndGetPlayer(login, password, localId);

        if (player is null)
        {
            SphLogger.Error($"SRV {localId:X4}: Incorrect password");
            streamPeerTcp.PutData(CommonPackets.CannotConnect(localId));
            clientConnection.Close();
            return;
        }

        if (!ActiveWorldObjects.LoggedInClients.TryAdd(login, 1))
        {
            SphLogger.Error($"SRV {localId:X4}: Already logged in. Login: {login}");
            streamPeerTcp.PutData(CommonPackets.CannotConnect(localId));
            clientConnection.Close();
            return;
        }

        clientConnection.SetPlayerDbEntry(player);

        player.Index = localId;

        SphLogger.Info($"SRV {localId:X4}: Fetched char list data");

        WaitForClientTimer = new (0.05, false, () =>
        {
            streamPeerTcp.PutData(CommonPackets.CharacterSelectStartData(localId));
            SphLogger.Info($"SRV {localId:X4}: Character select screen data - initial");

            // WaitForClientTimer.Arm(0.05, () =>
            // {
            var playerInitialData = new PlayerDbEntrySerializer(player).ToInitialDataByteArray();

            streamPeerTcp.PutData(playerInitialData);
            SphLogger.Info($"SRV {localId:X4}: Character select screen data - player characters");
            clientConnection.MoveToNextBeforeGameStage();
            // });
        });
    }
}