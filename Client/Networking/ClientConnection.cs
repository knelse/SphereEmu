using System.Threading.Tasks;
using BitStreams;
using Godot;
using SphServer.Client.Networking.Handlers;
using SphServer.Client.Networking.Handlers.BeforeGame;
using SphServer.Client.Networking.Handlers.InGame;
using SphServer.Client.Networking.Handlers.InGame.Chat;
using SphServer.Client.Networking.Handlers.InGame.Communities;
using SphServer.Client.Networking.Handlers.InGame.Containers;
using SphServer.Client.Networking.Handlers.InGame.DamageHealEffects;
using SphServer.Client.Networking.Handlers.InGame.Items;
using SphServer.Client.Networking.Handlers.InGame.NPC;
using SphServer.Client.Networking.Handlers.InGame.ObjectMovement;
using SphServer.Server.Config;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;

namespace SphServer.Client.Networking;

public class ClientConnection (StreamPeerTcp streamPeerTcp, ushort localId, SphereClient sphereClient)
{
    private PingHandler? pingHandler;
    private NpcInteractionHandler? npcInteractionHandler;
    private ISphereClientNetworkingHandler? currentHandler;
    private ClanActionsHandler? clanActionsHandler;
    private GroupActionsHandler? groupActionsHandler;
    private OpenLootContainerHandler? openLootContainerHandler;
    private ClientChatHandler? clientChatHandler;
    private PickupItemHandler? pickupItemHandler;
    private MoveItemHandler? moveItemHandler;
    private UseItemHandler? useItemHandler;
    private DropItemToGroundHandler? dropItemToGroundHandler;
    private MainhandTakeItemHandler? mainhandTakeItemHandler;
    private BuyItemFromTargetHandler? buyItemFromTargetHandler;
    private DamageTargetHandler? damageTargetHandler;
    private MoveObjectForClientHandler? moveObjectForClientHandler;
    private ChangeCharacterHealthHandler? changeCharacterHealthHandler;
    public readonly byte[] ReceiveBuffer = new byte [ServerConfig.AppConfig.ReceiveBufferSize];
    public BitStream DataStream = null!;

    public async Task Process (double delta)
    {
        InitHandlers();
        changeCharacterHealthHandler ??= new (localId, this);

        // handlers before game do their own data fetch. For ingame handlers, we need packet info here to figure out
        // which handler they should be routed to
        if (sphereClient.ClientStateManager.IsInGameState())
        {
            // keepalive always happens - it's time-based instead of client input based
            await pingHandler!.Keepalive(delta);
            var incomingDataLength = GetIncomingData();

            if (incomingDataLength == 0)
            {
                // client hasn't sent anything
                return;
            }

            DataStream = new BitStream(ReceiveBuffer);
            DataStream.CutStream(0, incomingDataLength);

            switch (ReceiveBuffer[0])
            {
                case 0x26:
                    await pingHandler.Handle(delta);
                    sphereClient.UpdateModelCoordinates();
                    break;
                case 0x13:
                    if (ReceiveBuffer[13] != 0x08 || ReceiveBuffer[14] != 0x40 || ReceiveBuffer[15] != 0x23 ||
                        ReceiveBuffer[16] != 0x23)
                    {
                        break;
                    }

                    await groupActionsHandler!.Handle(delta);
                    break;

                case 0x16:
                    if (ReceiveBuffer[13] == 0x08 && ReceiveBuffer[14] == 0x40 && ReceiveBuffer[15] == 0x23)
                    {
                        await pickupItemHandler!.HandlePickupToNextAvailableEmptySlot(delta);
                    }

                    break;
                case 0x18:
                    // move to a different slot
                    if (ReceiveBuffer[13] == 0x08 && ReceiveBuffer[14] == 0x40 && ReceiveBuffer[15] == 0x81)
                    {
                        await moveItemHandler!.Handle(delta);
                    }
                    // use item from inventory
                    else
                    {
                        await useItemHandler!.Handle(delta);
                    }

                    break;
                case 0x1A:
                    if (ReceiveBuffer[13] == 0x08 && ReceiveBuffer[14] == 0x40 && ReceiveBuffer[15] == 0x43)
                    {
                        // chat
                        await clientChatHandler!.Handle(delta);
                    }
                    else if (ReceiveBuffer[13] == 0x08 && ReceiveBuffer[14] == 0x40 &&
                             ReceiveBuffer[15] == 0xC1)
                    {
                        await pickupItemHandler!.HandlePickupToTargetSlot(delta);
                    }
                    else if (ReceiveBuffer[13] == 0x5c && ReceiveBuffer[14] == 0x46 && ReceiveBuffer[15] == 0xe1)
                    {
                        await openLootContainerHandler!.Handle(delta);
                    }

                    break;
                case 0x2D:
                    if (ReceiveBuffer[13] == 0x08 && ReceiveBuffer[14] == 0x40 && ReceiveBuffer[15] == 0x63)
                    {
                        await dropItemToGroundHandler!.Handle(delta);
                    }

                    break;
                case 0x31:
                case 0x36:
                    if (ReceiveBuffer[13] == 0x08 && ReceiveBuffer[14] == 0x40 && ReceiveBuffer[15] == 0xA3)
                    {
                        await npcInteractionHandler!.Handle(delta);
                    }

                    break;
                case 0x15:
                // case 0x19:
                case 0x1B:
                case 0x1F:
                case 0x23:
                    // item in hand
                    if (ReceiveBuffer[13] == 0x08 && ReceiveBuffer[14] == 0x40 &&
                        (ReceiveBuffer[15] == 0xA3 || ReceiveBuffer[15] == 0x83))
                    {
                        await mainhandTakeItemHandler!.Handle(delta);
                    }

                    break;
                case 0x19:
                case 0x20:
                    if (ReceiveBuffer[13] == 0x08 && ReceiveBuffer[14] == 0x40 && ReceiveBuffer[15] == 0x03)
                    {
                        await buyItemFromTargetHandler!.Handle(delta);
                    }
                    else
                    {
                        await damageTargetHandler!.Handle(delta);
                    }

                    break;
            }
        }

        else
        {
            await currentHandler!.Handle(delta);
        }
    }

    private void InitHandlers ()
    {
        currentHandler ??= new HandshakeHandler(streamPeerTcp, localId, this);
        pingHandler ??= new (streamPeerTcp, localId, this);
        npcInteractionHandler ??= new (localId, this);
        clanActionsHandler ??= new (localId, this);
        groupActionsHandler ??= new (localId, this);
        openLootContainerHandler ??= new (localId, this);
        clientChatHandler ??= new (localId, this);
        pickupItemHandler ??= new (localId, this);
        moveItemHandler ??= new (localId, this);
        useItemHandler ??= new (localId, this);
        dropItemToGroundHandler ??= new (localId, this);
        mainhandTakeItemHandler ??= new (localId, this);
        buyItemFromTargetHandler ??= new (localId, this);
        damageTargetHandler ??= new (localId, this);
        moveObjectForClientHandler ??= new (localId, this);
    }

    public void MoveToNextBeforeGameStage ()
    {
        SphLogger.Info(
            $"Client moved from state: {sphereClient.ClientStateManager.CurrentState}. Client ID: {localId:X4}");
        sphereClient.ClientStateManager.Transition();
        currentHandler =
            BeforeGameHandlers.GetHandlerForState(sphereClient.ClientStateManager.CurrentState, streamPeerTcp, localId,
                this);
        var handlerNameStr = currentHandler?.ToString() ?? "{none}";
        SphLogger.Info(
            $"New state: {sphereClient.ClientStateManager.CurrentState}. New handler: {handlerNameStr}. Client ID: {localId:X4}");
    }

    public void Close ()
    {
        sphereClient.RemoveClient();
    }

    public int GetIncomingData ()
    {
        var temp = streamPeerTcp.GetPartialData(ServerConfig.AppConfig.ReceiveBufferSize);
        var arr = (byte[]?) temp[1];

        var i = 0;

        if (arr is not null)
        {
            for (; i < arr.Length; i++)
            {
                ReceiveBuffer[i] = arr[i];
            }
        }

        return i;
    }

    public void SetPlayerDbEntry (PlayerDbEntry? entry)
    {
        sphereClient.SetPlayerDbEntry(entry);
    }

    public void DeletePlayerCharacter (int index)
    {
        sphereClient.DeletePlayerCharacter(index);
    }

    public void CreatePlayerCharacter (CharacterDbEntry newCharacter, int index)
    {
        sphereClient.CreatePlayerCharacter(newCharacter, index);
    }

    public void SetSelectedCharacterIndex (int index)
    {
        sphereClient.SetSelectedCharacterIndex(index);
    }

    public CharacterDbEntry? GetSelectedCharacter ()
    {
        return sphereClient.GetSelecterCharacter();
    }

    public void MaybeQueueNetworkPacketSend (byte[] packet)
    {
        // TODO: might need an actual queue. For now, just send
        streamPeerTcp.PutData(packet);
    }
}