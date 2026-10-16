using System;
using BitStreams;
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

public class LoginDataHandler (Guid entityId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    private SphereTimer? WaitForClientTimer;

    public async Task Handle (byte[] frame, double delta)
    {
        if (WaitForClientTimer is not null)
        {
            WaitForClientTimer.Tick (delta);
            return;
        }

        if (frame.Length <= 12)
        {
            return;
        }

        SphLogger.Info ($"CLI {entityId}: Login data sent");
        var (login, password) = LoginDecoder.DecodeFromBuffer (frame, new BitStream (frame));
        if (string.IsNullOrEmpty (login) || string.IsNullOrEmpty (password))
        {
            SphLogger.Error ($"SRV {entityId}: Invalid login.");
            clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.CannotConnect (entityId));
            clientConnection.Close ();
            return;
        }

        if (BannedClients.IsLoginBanned (login))
        {
            SphLogger.Error ($"SRV {entityId}: Login is banned. Login: {login}");
            clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.CannotConnect (entityId));
            clientConnection.Close ();
            return;
        }

        var player =
            LoginManager.CheckLoginAndGetPlayer (login, password, entityId);

        if (player is null)
        {
            SphLogger.Error ($"SRV {entityId}: Incorrect password");
            clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.CannotConnect (entityId));
            clientConnection.Close ();
            return;
        }

        if (!ActiveWorldObjects.LoggedInClients.TryAdd (login, 1))
        {
            SphLogger.Error ($"SRV {entityId}: Already logged in. Login: {login}");
            clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.CannotConnect (entityId));
            clientConnection.Close ();
            return;
        }

        clientConnection.SetPlayerDbEntry (player);

        player.Index = entityId;

        SphLogger.Info ($"SRV {entityId}: Fetched char list data");

        WaitForClientTimer = new (0.05, false, () =>
        {
            clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.CharacterSelectStartData (entityId));
            SphLogger.Info ($"SRV {entityId}: Character select screen data - initial");

            // WaitForClientTimer.Arm(0.05, () =>
            // {
            var playerInitialData = new PlayerDbEntrySerializer (player).ToInitialDataByteArray (entityId);

            clientConnection.MaybeScheduleNetworkPacketSend (playerInitialData);
            SphLogger.Info ($"SRV {entityId}: Character select screen data - player characters");
            clientConnection.MoveToNextBeforeGameStage ();
            // });
        });
    }
}
