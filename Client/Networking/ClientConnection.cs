using System;
using System.Buffers.Binary;
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
using SphServer.Client.Networking.Handlers.InGame.Mutator;
using SphServer.Client.Networking.Handlers.InGame.NPC;
using SphServer.Client.Networking.Handlers.InGame.ObjectMovement;
using SphServer.Helpers.Networking;
using SphServer.Packets;
using SphServer.Server.Config;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;
using SphServer.Godot.Scripts.Objects.HelperGizmos;
using SphServer.Godot.Scripts.World;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Client.Networking;

public class ClientConnection(StreamPeerTcp streamPeerTcp, ushort localId, SphereClient sphereClient)
{
    public ushort LocalId => localId;

    public readonly byte[] ReceiveBuffer = new byte[ServerConfig.AppConfig.ReceiveBufferSize];
    private BuyItemFromTargetHandler? buyItemFromTargetHandler;
    private ChangeCharacterHealthHandler? changeCharacterHealthHandler;
    private ClanActionsHandler? clanActionsHandler;
    private ClientChatHandler? clientChatHandler;
    private ISphereClientNetworkingHandler? currentHandler;
    private DamageTargetHandler? damageTargetHandler;
    public BitStream DataStream = null!;
    private DragItemOnGroundHandler? dragItemOnGroundHandler;
    private DropItemToGroundHandler? dropItemToGroundHandler;
    private GroupActionsHandler? groupActionsHandler;
    private bool interactionWithOtherObjectsInitialized;
    private bool seenFirstPositionKeepalive;
    private double timeSinceFirstPositionKeepalive;
    private bool starterMutatorSent;
    private MainhandTakeItemHandler? mainhandTakeItemHandler;
    private MoveItemHandler? moveItemHandler;
    private MoveObjectForClientHandler? moveObjectForClientHandler;
    private NpcInteractionHandler? npcInteractionHandler;
    private OpenLootContainerHandler? openLootContainerHandler;
    private PickupItemHandler? pickupItemHandler;
    private PingHandler? pingHandler;
    private UseItemHandler? useItemHandler;

    /// <summary>
    ///     Chat arrives as a 0x1A header plus continuation frames that often land in later TCP reads.
    ///     Retail only replies after the full send is in; answering early causes lost/duped lines.
    /// </summary>
    private readonly List<byte> pendingChatBytes = [];

    public async Task Process(double delta)
    {
        InitHandlers();
        changeCharacterHealthHandler ??= new(localId, this);

        // handlers before game do their own data fetch. For ingame handlers, we need packet info here to figure out
        // which handler they should be routed to
        if (sphereClient.ClientStateManager.IsInGameState())
        {
            if (!interactionWithOtherObjectsInitialized)
            {
                sphereClient.InitializeInteractions();
                interactionWithOtherObjectsInitialized = true;
                sphereClient.UpdateCoordinatesInWorld();
                MonsterSpawnerActivationManager.NotifyClientPosition(sphereClient);
                AlchemyMaterialSpawnerActivationManager.NotifyClientPosition(sphereClient);
                WorldObjectVisibilityManager.NotifyClientPosition(sphereClient);
                SphServer.Server.SphereServer.ServerNode?.WorldChunks?.NotifyClientPosition(sphereClient);
                SphServer.Server.SphereServer.ServerNode?.TerrainGround?.NotifyClientPosition(sphereClient);
            }

            if (seenFirstPositionKeepalive)
            {
                timeSinceFirstPositionKeepalive += delta;
                MaybeSendStarterMutator();
            }

            // keepalive always happens - it's time-based instead of client input based
            await pingHandler!.Keepalive(delta);
            clientChatHandler?.FlushPendingReply();
            var incomingDataLength = GetIncomingData();

            if (incomingDataLength == 0)
            {
                // client hasn't sent anything
                return;
            }

            if (pendingChatBytes.Count > 0 || ClientChatHandler.BufferStartsWithChatHeader(ReceiveBuffer, incomingDataLength))
            {
                await ProcessPossiblyFragmentedChat(incomingDataLength, delta);
                return;
            }

            // Classify the leading frame (ushort length — high byte matters for frames > 255).
            var frameLength = ReceiveBuffer[0] | (ReceiveBuffer[1] << 8);
            var frame = frameLength >= 2 && frameLength <= incomingDataLength
                ? ReceiveBuffer.AsSpan(0, frameLength)
                : ReceiveBuffer.AsSpan(0, incomingDataLength);
            var classification = ClientPacketClassifier.ClassifyFrame(frame);
            if (!classification.IsEvent)
            {
                // Otherwise a frame with no route looks exactly like an idle client.
                if (ServerConfig.AppConfig.DebugMode && frame.Length >= 16)
                {
                    SphLogger.Debug(
                        $"C->S unrouted {frame.Length}B frame, signature " +
                        $"{frame[13]:X2} {frame[14]:X2} {frame[15]:X2} ({classification.Reason}): " +
                        $"{Convert.ToHexString(frame[..Math.Min(24, frame.Length)])}. Client ID: {localId:X4}");
                }

                return;
            }

            await DispatchClientPacketEvent(classification.Event, delta);
        }

        else
        {
            await currentHandler!.Handle(delta);
        }
    }

    private async Task ProcessPossiblyFragmentedChat(int incomingDataLength, double delta)
    {
        var offset = 0;
        while (offset + 2 <= incomingDataLength)
        {
            var frameLength = ReceiveBuffer[offset] | (ReceiveBuffer[offset + 1] << 8);
            if (frameLength < 2 || offset + frameLength > incomingDataLength)
            {
                break;
            }

            var frame = ReceiveBuffer.AsSpan(offset, frameLength).ToArray();
            offset += frameLength;

            if (ClientChatHandler.IsChatHeaderFrame(frame) ||
                (pendingChatBytes.Count > 0 && ClientChatHandler.IsChatContinuationFrame(frame)))
            {
                if (ClientChatHandler.IsChatHeaderFrame(frame))
                {
                    pendingChatBytes.Clear();
                }

                pendingChatBytes.AddRange(frame);

                if (ClientChatHandler.IsChatSendComplete(pendingChatBytes))
                {
                    var complete = pendingChatBytes.ToArray();
                    pendingChatBytes.Clear();
                    complete.CopyTo(ReceiveBuffer, 0);
                    DataStream = new BitStream(ReceiveBuffer);
                    DataStream.CutStream(0, complete.Length);
                    await clientChatHandler!.Handle(delta);
                }

                continue;
            }

            // Interleaved non-chat (e.g. 0x26 ping) while assembling — dispatch immediately.
            frame.CopyTo(ReceiveBuffer, 0);
            DataStream = new BitStream(ReceiveBuffer);
            DataStream.CutStream(0, frame.Length);
            var classification = ClientPacketClassifier.ClassifyFrame(frame);
            if (classification.IsEvent && classification.Event != ClientPacketEvent.ChatSend)
            {
                await DispatchClientPacketEvent(classification.Event, delta);
            }
        }
    }

    private async Task DispatchClientPacketEvent(ClientPacketEvent packetEvent, double delta)
    {
        switch (packetEvent)
        {
            case ClientPacketEvent.PositionKeepalive:
                seenFirstPositionKeepalive = true;
                await pingHandler!.Handle(delta);
                sphereClient.UpdateCoordinatesInWorld();
                break;
            case ClientPacketEvent.GroupAction:
                await groupActionsHandler!.Handle(delta);
                break;
            case ClientPacketEvent.ItemPickup:
                await pickupItemHandler!.HandlePickupToNextAvailableEmptySlot(delta);
                break;
            case ClientPacketEvent.ItemMove:
                await moveItemHandler!.Handle(delta);
                break;
            case ClientPacketEvent.ItemUse:
                await useItemHandler!.Handle(delta);
                break;
            case ClientPacketEvent.ChatSend:
                await clientChatHandler!.Handle(delta);
                break;
            case ClientPacketEvent.ItemPickupToSlot:
                await pickupItemHandler!.HandlePickupToTargetSlot(delta);
                break;
            case ClientPacketEvent.ContainerOpenLoot:
                await openLootContainerHandler!.Handle(delta);
                break;
            case ClientPacketEvent.ItemDrop:
                await dropItemToGroundHandler!.Handle(delta);
                break;
            case ClientPacketEvent.ItemDragOnGround:
                await dragItemOnGroundHandler!.Handle(delta);
                break;
            case ClientPacketEvent.NpcInteract:
                await npcInteractionHandler!.Handle(delta);
                break;
            case ClientPacketEvent.ItemTakeMainhand:
                await mainhandTakeItemHandler!.Handle(delta);
                break;
            case ClientPacketEvent.TradeBuy:
                await buyItemFromTargetHandler!.Handle(delta);
                break;
            case ClientPacketEvent.CombatDamageTarget:
                await damageTargetHandler!.Handle(delta);
                break;
            case ClientPacketEvent.ProtocolControl:
                // Short control frames — no gameplay handler.
                break;
        }
    }

    private void InitHandlers()
    {
        currentHandler ??= new HandshakeHandler(localId, this);
        pingHandler ??= new(streamPeerTcp, localId, this);
        npcInteractionHandler ??= new(localId, this);
        clanActionsHandler ??= new();
        groupActionsHandler ??= new(this);
        openLootContainerHandler ??= new(localId, this);
        clientChatHandler ??= new(this);
        pickupItemHandler ??= new(localId, this);
        dragItemOnGroundHandler ??= new(localId, this);
        moveItemHandler ??= new(localId, this);
        useItemHandler ??= new(localId, this);
        dropItemToGroundHandler ??= new();
        mainhandTakeItemHandler ??= new();
        buyItemFromTargetHandler ??= new();
        damageTargetHandler ??= new(localId, this);
        moveObjectForClientHandler ??= new(this);
    }

    public void MoveToNextBeforeGameStage()
    {
        SphLogger.Info(
            $"Client moved from state: {sphereClient.ClientStateManager.CurrentState}. Client ID: {localId:X4}");
        sphereClient.ClientStateManager.Transition();
        currentHandler =
            BeforeGameHandlers.GetHandlerForState(sphereClient.ClientStateManager.CurrentState, localId,
                this);
        var handlerNameStr = currentHandler?.ToString() ?? "{none}";
        SphLogger.Info(
            $"New state: {sphereClient.ClientStateManager.CurrentState}. New handler: {handlerNameStr}. Client ID: {localId:X4}");
    }

    public void Close()
    {
        sphereClient.RemoveClient();
    }

    public int GetIncomingData()
    {
        // var packetInput = Convert.FromHexString(
        //     "1A005AF0ED022C0100710AA7A364B027B4169B8DC8CD936E98DE1101E2F8EE022C0100710AA7A364B02754E82B8DEFE04EEC1B8BCF942F204093A1EC017FD2261C5BCBE1BBE9E3A61EC33B0995652349C9C3E7D27472A93DE82886C6CB3691752B3C8E772644B1588B32184D916A21A0F4B2FB165FC1247B4937E74F838FA1A0188EB3C2F741BE76B6D40D6B7D778A70095847D3C8FDC2801D16E37B5BAF4E459C4860A52E74C7B1D487AB8DF7231C917CBA4702286FB9E211B385E786BEF4EC7B0EF00EFB8B064545B1972D73074C17A586369CB1CAE9FE162CA2EBB39B42F3CC30DF01F4A1E0B6DB64437413B1259CBD2ABE4BC1D51E5DDDFBEFB0D46FC0D09883CAF811368FB54515914B4A879DFE33E2049CAE93833E682229B8A6074C87FA96B750619ABD7EF48FDB5D0022F3F0022C0100710AA7A364B027D47E7B0D6FC0E1BA98FE9B4B0695B561A79DB357FC4795E6D60C81DA1AA9D3A8A12C965EBCF466694B61B789D0F585B0817AC0CEC7C1B3F1C25A34D74BBD2952745DF3122E2C18D20A0B5B2DAF");
        var temp = streamPeerTcp.GetPartialData(ServerConfig.AppConfig.ReceiveBufferSize);
        var arr = (byte[]?)temp[1];
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
                    SphPacketLogger.LogIncoming(localId, subpacket);

                    var shouldDecode = ShouldDecodeClientSubpacket(subpacket, localId);
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
            var output = arr is { Length: > 0 } ? Convert.ToHexString(arr) : "<empty>";
            SphLogger.Error($"Incorrect packet from client: {output}. Client ID: {localId}", ex);
            ReceiveBuffer[0] = 0;
            return 0;
        }
    }

    public void SetPlayerDbEntry(PlayerDbEntry? entry)
    {
        sphereClient.SetPlayerDbEntry(entry);
    }

    public void SaveSelectedCharacter()
    {
        sphereClient.SaveCharacter();
    }

    public void DeletePlayerCharacter(int index)
    {
        sphereClient.DeletePlayerCharacter(index);
    }

    public void CreatePlayerCharacter(CharacterDbEntry newCharacter, int index)
    {
        sphereClient.CreatePlayerCharacter(newCharacter, index);
    }

    public void SetSelectedCharacterIndex(int index)
    {
        sphereClient.SetSelectedCharacterIndex(index);
    }

    public CharacterDbEntry? GetSelectedCharacter()
    {
        return sphereClient.GetSelecterCharacter();
    }

    public void SendPacket(byte[] packet)
    {
        SphPacketLogger.LogOutgoing(localId, packet);
        streamPeerTcp.PutData(packet);
    }

    public void MaybeScheduleNetworkPacketSend(byte[] packet)
    {
        // TODO: might need an actual queue. For now, just send
        SendPacket(packet);
    }

    private void MaybeSendStarterMutator()
    {
        if (starterMutatorSent || timeSinceFirstPositionKeepalive < 3.0)
        {
            return;
        }

        MutatorHandler.SendSpecialMutator(this, localId, SpecialMutator.Прыг_х4, BelongingSlot.Mutator_1);
        MutatorHandler.SendSpecialMutator(this, localId, SpecialMutator.СХ, BelongingSlot.Mutator_2);
        starterMutatorSent = true;
    }

    public void EnqueueClientEvent(ClientQueuedEvent clientEvent)
    {
        sphereClient.EnqueueClientEvent(clientEvent);
    }

    private static bool ShouldDecodeClientSubpacket(byte[] subpacket, ushort localId)
    {
        if (subpacket.Length <= 12)
        {
            return false;
        }

        return !ClientSubpacketReferencesLocalPlayer(subpacket, localId);
    }

    private static bool ClientSubpacketReferencesLocalPlayer(byte[] subpacket, ushort localId)
    {
        var localIdHigh = (byte)(localId >> 8);
        var localIdLow = (byte)(localId & 0xFF);

        if (subpacket[11] == localIdHigh && subpacket[12] == localIdLow)
        {
            return true;
        }

        if (subpacket.Length > 8 && subpacket[7] == localIdHigh && subpacket[8] == localIdLow)
        {
            return true;
        }

        // Main ping is wire-format and stores client id at 16–18, not 11–12.
        if (subpacket.Length == 0x26
            && TryGetPingPackedClientId(subpacket, out var pingClientId)
            && pingClientId == localId)
        {
            return true;
        }

        return false;
    }

    private static bool TryGetPingPackedClientId(byte[] packet, out ushort clientId)
    {
        clientId = 0;
        if (packet.Length <= 18)
        {
            return false;
        }

        // Bit field yields major/minor swapped vs localId; reverse to match.
        var packed = (ushort)((packet[16] >> 5) + (packet[17] << 3) + ((packet[18] & 0b11111) << 11));
        clientId = BinaryPrimitives.ReverseEndianness(packed);
        return true;
    }
}