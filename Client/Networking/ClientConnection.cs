using System;
using System.Collections.Generic;
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
using SphServer.Packets;
using SphServer.Server.Config;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;

namespace SphServer.Client.Networking;

public class ClientConnection (StreamPeerTcp streamPeerTcp, ushort localId, SphereClient sphereClient)
{
    public readonly byte[] ReceiveBuffer = new byte [ServerConfig.AppConfig.ReceiveBufferSize];
    private BuyItemFromTargetHandler? buyItemFromTargetHandler;
    private ChangeCharacterHealthHandler? changeCharacterHealthHandler;
    private ClanActionsHandler? clanActionsHandler;
    private ClientChatHandler? clientChatHandler;
    private ISphereClientNetworkingHandler? currentHandler;
    private DamageTargetHandler? damageTargetHandler;
    public BitStream DataStream = null!;
    private DropItemToGroundHandler? dropItemToGroundHandler;
    private GroupActionsHandler? groupActionsHandler;
    private bool interactionWithOtherObjectsInitialized;
    private MainhandTakeItemHandler? mainhandTakeItemHandler;
    private MoveItemHandler? moveItemHandler;
    private MoveObjectForClientHandler? moveObjectForClientHandler;
    private NpcInteractionHandler? npcInteractionHandler;
    private OpenLootContainerHandler? openLootContainerHandler;
    private PickupItemHandler? pickupItemHandler;
    private PingHandler? pingHandler;
    private UseItemHandler? useItemHandler;

    public async Task Process (double delta)
    {
        InitHandlers();
        changeCharacterHealthHandler ??= new (localId, this);

        // handlers before game do their own data fetch. For ingame handlers, we need packet info here to figure out
        // which handler they should be routed to
        if (sphereClient.ClientStateManager.IsInGameState())
        {
            if (!interactionWithOtherObjectsInitialized)
            {
                sphereClient.InitializeInteractions();
                interactionWithOtherObjectsInitialized = true;
            }

            // keepalive always happens - it's time-based instead of client input based
            await pingHandler!.Keepalive(delta);
            var incomingDataLength = GetIncomingData();

            if (incomingDataLength == 0)
            {
                // client hasn't sent anything
                return;
            }

            switch (ReceiveBuffer[0])
            {
                case 0x26:
                    await pingHandler.Handle(delta);
                    sphereClient.UpdateCoordinatesInWorld();
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
        // var packetInput = Convert.FromHexString(
        //     "1A005AF0ED022C0100710AA7A364B027B4169B8DC8CD936E98DE1101E2F8EE022C0100710AA7A364B02754E82B8DEFE04EEC1B8BCF942F204093A1EC017FD2261C5BCBE1BBE9E3A61EC33B0995652349C9C3E7D27472A93DE82886C6CB3691752B3C8E772644B1588B32184D916A21A0F4B2FB165FC1247B4937E74F838FA1A0188EB3C2F741BE76B6D40D6B7D778A70095847D3C8FDC2801D16E37B5BAF4E459C4860A52E74C7B1D487AB8DF7231C917CBA4702286FB9E211B385E786BEF4EC7B0EF00EFB8B064545B1972D73074C17A586369CB1CAE9FE162CA2EBB39B42F3CC30DF01F4A1E0B6DB64437413B1259CBD2ABE4BC1D51E5DDDFBEFB0D46FC0D09883CAF811368FB54515914B4A879DFE33E2049CAE93833E682229B8A6074C87FA96B750619ABD7EF48FDB5D0022F3F0022C0100710AA7A364B027D47E7B0D6FC0E1BA98FE9B4B0695B561A79DB357FC4795E6D60C81DA1AA9D3A8A12C965EBCF466694B61B789D0F585B0817AC0CEC7C1B3F1C25A34D74BBD2952745DF3122E2C18D20A0B5B2DAF");
        var temp = streamPeerTcp.GetPartialData(ServerConfig.AppConfig.ReceiveBufferSize);
        var arr = (byte[]?) temp[1];
        try
        {
            var resultLength = 0;

            if (arr is not null && arr.Length > 0)
            {
                var subpackets = new List<byte[]>();
                var decodedSubpackets = new List<byte>();
                for (var i = 0; i < arr.Length;)
                {
                    var packetLength = arr[i + 1] * 256 + arr[i];
                    subpackets.Add(arr[i..(i + packetLength)]);
                    i += packetLength;
                }

                foreach (var subpacket in subpackets)
                {
                    var shouldDecode = subpacket.Length > 12 &&
                                       (subpacket[11] != localId >> 8 || subpacket[12] != (localId & 0b11111111));
                    var currentDecode = shouldDecode ? Packet.DecodeClientPacket(subpacket) : subpacket;
                    decodedSubpackets.AddRange(currentDecode);
                }

                var decoded = decodedSubpackets.ToArray();

                for (; resultLength < decoded.Length; resultLength++)
                {
                    ReceiveBuffer[resultLength] = decoded[resultLength];
                }

                DataStream = new BitStream(ReceiveBuffer);
                DataStream.CutStream(0, decoded.Length);
            }
            else
            {
                ReceiveBuffer[0] = 0;
            }

            return resultLength;
        }
        catch (Exception ex)
        {
            var output = (arr?.Length ?? 0) > 0 ? Convert.ToHexString(arr) : "<empty>";
            SphLogger.Error($"Incorrect packet from client: {output}. Client ID: {localId}", ex);
            ReceiveBuffer[0] = 0;
            return 0;
        }
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

    public void MaybeScheduleNetworkPacketSend (byte[] packet)
    {
        // TODO: might need an actual queue. For now, just send
        streamPeerTcp.PutData(packet);
    }
}