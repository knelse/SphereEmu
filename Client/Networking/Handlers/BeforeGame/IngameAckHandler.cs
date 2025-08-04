using System;
using System.Threading.Tasks;
using Godot;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.System;

namespace SphServer.Client.Networking.Handlers.BeforeGame;

public class IngameAckHandler (StreamPeerTcp streamPeerTcp, ushort localId, ClientConnection clientConnection)
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
        
        if (clientConnection.GetIncomingData() != 0x13)
        {
            return;
        }
        
        var character = clientConnection.GetSelectedCharacter();

        if (character is null)
        {
            // should never happen
            SphLogger.Error($"SRV {localId:X4}: Selected character is null");
            return;
        }
        
        SphLogger.Info($"SRV {localId:X4}: Sending game world data");
        
        var worldData = CommonPackets.NewCharacterWorldData(character.ClientIndex);
        streamPeerTcp.PutData(worldData[0]);
        streamPeerTcp.PutData(Convert.FromHexString(
            "BA002C010000004F6F08C002D07911C8BD10445E0C222F08C91685C80B03581CC002011609B05080C5022C1860D1000B07593CC802021611B09080C5042C286051010B0B585CC00213799189BCD0445E6CC08203161DB0F080C5072C406011020B11588CC882441625B03081C5892D506091020B1558AC422C5870D1820B1758CCD082061635B0B0C1C603848F1535B10F2B6391702035D1F643F24F411072A0D901900100000A5290530F0000D0001170AA2A48410E32000000"));
        streamPeerTcp.PutData(Convert.FromHexString(
            "83002C010000004F6F08406102000A824011820E400600005010841C000000000000808220E888A00300000000140461A70B1D0068890920445DE8000005419820480768010000280802402D3B007D0000404110706CD901060000000A120050908089820450142400A720013B0541A0C041072003000068E010280000003436020200"));

        WaitForClientTimer = new (0.05f, false, clientConnection.MoveToNextBeforeGameStage);
    }
}