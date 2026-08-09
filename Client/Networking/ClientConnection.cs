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

    private BuyItemFromTargetHandler? buyItemFromTargetHandler;
    private ChangeCharacterHealthHandler? changeCharacterHealthHandler;
    private ClanActionsHandler? clanActionsHandler;
    private ClientChatHandler? clientChatHandler;
    private ISphereClientNetworkingHandler? currentHandler;
    private DamageTargetHandler? damageTargetHandler;
    private DragItemOnGroundHandler? dragItemOnGroundHandler;
    private DropItemToGroundHandler? dropItemToGroundHandler;
    private GroupActionsHandler? groupActionsHandler;
    private bool interactionWithOtherObjectsInitialized;
    private bool seenFirstPositionKeepalive;
    private double timeSinceFirstPositionKeepalive;
    private bool starterMutatorSent;
    private MainhandTakeItemHandler? mainhandTakeItemHandler;
    private SwapItemHandler? swapItemHandler;
    private MoveItemHandler? moveItemHandler;
    private MoveObjectForClientHandler? moveObjectForClientHandler;
    private NpcInteractionHandler? npcInteractionHandler;
    private OpenLootContainerHandler? openLootContainerHandler;
    private PickupItemHandler? pickupItemHandler;
    private PingHandler? pingHandler;
    private UseItemHandler? useItemHandler;

    /// <summary>Holds the byte stream between reads, so a frame split across two of them survives.</summary>
    private readonly ClientFrameReader frameReader = new();

    /// <summary>
    ///     Whole frames off the wire that no handler has taken yet. In game every frame of a tick
    ///     is dispatched; before it, handlers get one per tick, because each of those states waits
    ///     for one particular frame and anything arriving with it belongs to the next state.
    /// </summary>
    private readonly Queue<byte[]> pendingFrames = new();

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
            ReadFrames();

            while (pendingFrames.Count > 0)
            {
                await DispatchFrame(pendingFrames.Dequeue(), delta);
            }
        }

        else
        {
            ReadFrames();
            await currentHandler!.Handle(pendingFrames.Count > 0 ? pendingFrames.Dequeue() : [], delta);
        }
    }

    /// <summary>One whole frame: classified by what it says it is, then routed.</summary>
    private async Task DispatchFrame(byte[] frame, double delta)
    {
        var classification = ClientPacketClassifier.ClassifyFrame(frame);
        if (!classification.IsEvent)
        {
            // Otherwise a frame with no route looks exactly like an idle client.
            if (ServerConfig.AppConfig.DebugMode && frame.Length >= 16)
            {
                SphLogger.Debug(
                    $"C->S unrouted {frame.Length}B frame, signature " +
                    $"{frame[13]:X2} {frame[14]:X2} {frame[15]:X2} ({classification.Reason}): " +
                    // Whole frame: this line exists to capture what we cannot decode yet, and
                    // the fields that identify an unknown frame are usually past its head.
                    $"{Convert.ToHexString(frame)}. Client ID: {localId:X4}");
            }

            return;
        }

        await DispatchClientPacketEvent(classification.Event, frame, delta);
    }

    private async Task DispatchClientPacketEvent(ClientPacketEvent packetEvent, byte[] frame, double delta)
    {
        switch (packetEvent)
        {
            case ClientPacketEvent.PositionKeepalive:
                seenFirstPositionKeepalive = true;
                await pingHandler!.Handle(frame, delta);
                sphereClient.UpdateCoordinatesInWorld();
                break;
            case ClientPacketEvent.GroupAction:
                await groupActionsHandler!.Handle(frame, delta);
                break;
            case ClientPacketEvent.ItemPickup:
                await pickupItemHandler!.HandlePickupToNextAvailableEmptySlot(frame, delta);
                break;
            case ClientPacketEvent.ItemMove:
                await moveItemHandler!.Handle(frame, delta);
                break;
            case ClientPacketEvent.ItemUse:
                await useItemHandler!.Handle(frame, delta);
                break;
            case ClientPacketEvent.ChatSend:
                await clientChatHandler!.Accept(frame, delta);
                break;
            case ClientPacketEvent.ItemPickupToSlot:
                await pickupItemHandler!.HandlePickupToTargetSlot(frame, delta);
                break;
            case ClientPacketEvent.ContainerOpenLoot:
                await openLootContainerHandler!.Handle(frame, delta);
                break;
            case ClientPacketEvent.ItemDrop:
                await dropItemToGroundHandler!.Handle(frame, delta);
                break;
            case ClientPacketEvent.ItemDragOnGround:
                await dragItemOnGroundHandler!.Handle(frame, delta);
                break;
            case ClientPacketEvent.NpcInteract:
                await npcInteractionHandler!.Handle(frame, delta);
                break;
            case ClientPacketEvent.ItemTakeMainhand:
                await mainhandTakeItemHandler!.Handle(frame, delta);
                break;
            case ClientPacketEvent.ItemSwap:
                await swapItemHandler!.Handle(frame, delta);
                break;
            case ClientPacketEvent.TradeBuy:
                await buyItemFromTargetHandler!.Handle(frame, delta);
                break;
            case ClientPacketEvent.CombatDamageTarget:
                await damageTargetHandler!.Handle(frame, delta);
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
        mainhandTakeItemHandler ??= new(localId, this);
        swapItemHandler ??= new(localId, this);
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

    /// <summary>
    ///     Takes what the socket has and turns it into whole frames: TCP has no message
    ///     boundaries, so a read can end mid-frame and can carry several.
    /// </summary>
    private void ReadFrames()
    {
        var temp = streamPeerTcp.GetPartialData(ServerConfig.AppConfig.ReceiveBufferSize);
        var incoming = (byte[]?)temp[1];
        if (incoming is { Length: > 0 })
        {
            frameReader.Append(incoming);
        }

        while (true)
        {
            var result = frameReader.TryTake(out var frame);

            if (result == FrameReadResult.Incomplete)
            {
                return;
            }

            if (result == FrameReadResult.Desynced)
            {
                SphLogger.Error(
                    $"Lost the frame boundary: {frameReader.DesyncReason}. " +
                    $"{frameReader.Pending} bytes held, {pendingFrames.Count} frames read first. " +
                    $"Closing. Client ID: {localId:X4}");
                Close();

                // The frames already read are valid, but dispatching into a connection being torn
                // down is not worth them.
                pendingFrames.Clear();
                return;
            }

            SphPacketLogger.LogIncoming(localId, frame);
            pendingFrames.Enqueue(ShouldDecodeClientSubpacket(frame, localId)
                ? Packet.DecodeClientPacket(frame)
                : frame);
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