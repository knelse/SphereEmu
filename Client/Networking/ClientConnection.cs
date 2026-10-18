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
using SphServer.Client.Networking.Handlers.InGame.PlayerCharacter;
using SphServer.Helpers.Networking;
using SphServer.Packets;
using SphServer.Server.Broadcast;
using SphServer.Server.Config;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.Networking.DataModel.Serializers;
using SphServer.Shared.WorldState;
using SphServer.Godot.Scripts.Objects.HelperGizmos;
using SphServer.Godot.Scripts.World;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Client.Networking;

public class ClientConnection (SphereClient sphereClient)
{
    private bool secureResume;
    private Action<byte[]>? beforeGameSend;

    private BuyItemFromTargetHandler? buyItemFromTargetHandler;
    private ChangeStatsHandler? changeStatsHandler;
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
    private bool playerCountPublished;

    /// <summary>
    /// True after the client has sent its first in-world position keepalive.
    /// </summary>
    public bool HasSeenFirstPositionKeepalive => seenFirstPositionKeepalive;
    private MainhandTakeItemHandler? mainhandTakeItemHandler;
    private SwapItemHandler? swapItemHandler;
    private MoveItemHandler? moveItemHandler;
    private MoveObjectForClientHandler? moveObjectForClientHandler;
    private NpcInteractionHandler? npcInteractionHandler;
    private OpenLootContainerHandler? openLootContainerHandler;
    private PickupItemHandler? pickupItemHandler;
    private PingHandler? pingHandler;
    private UseItemHandler? useItemHandler;

    /// <summary>
    /// Holds the byte stream between reads, so a frame split across two of them survives.
    /// </summary>
    private readonly ClientFrameReader frameReader = new ();

    /// <summary>
    /// Before game each state takes one frame, so anything that arrived with it stays queued
    /// </summary>
    private readonly Queue<byte[]> pendingFrames = new ();

    public async Task Process (double delta)
    {
        InitHandlers ();

        // handlers before game do their own data fetch. For ingame handlers, we need packet info
        // here to figure out
        // which handler they should be routed to
        if (sphereClient.ClientStateManager.IsInGameState ())
        {
            if (!playerCountPublished)
            {
                playerCountPublished = true;
                PlayerCountBroadcast.OnClientEnteredWorld (sphereClient);
            }

            if (!interactionWithOtherObjectsInitialized)
            {
                // Pose must be correct before base._Ready registers this client in the visibility
                // grid.
                sphereClient.UpdateCoordinatesInWorld ();
                sphereClient.InitializeInteractions ();
                interactionWithOtherObjectsInitialized = true;
                // Re-register after Ready in case _Ready ran before Origin was applied.
                sphereClient.UpdateCoordinatesInWorld ();
                MonsterSpawnerActivationManager.NotifyClientPosition (sphereClient);
                AlchemyMaterialSpawnerActivationManager.NotifyClientPosition (sphereClient);
                WorldObjectVisibilityManager.NotifyClientPosition (sphereClient);
                SphServer.Server.SphereServer.ServerNode?.WorldChunks?.NotifyClientPosition (sphereClient);
                SphServer.Server.SphereServer.ServerNode?.TerrainGround?.NotifyClientPosition (sphereClient);
            }

            if (seenFirstPositionKeepalive)
            {
                timeSinceFirstPositionKeepalive += delta;
                MaybeSendStarterMutator ();
            }

            // keepalive always happens - it's time-based instead of client input based
            await pingHandler!.Keepalive (delta);
            clientChatHandler?.FlushPendingReply ();

            while (pendingFrames.Count > 0)
            {
                await DispatchFrame (pendingFrames.Dequeue (), delta);
            }
        }

        else
        {
            await currentHandler!.Handle (pendingFrames.Count > 0 ? pendingFrames.Dequeue () : [], delta);
        }
    }

    private async Task DispatchFrame (byte[] frame, double delta)
    {
        var classification = ClientPacketClassifier.ClassifyFrame (frame);
        if (!classification.IsEvent)
        {
            // Otherwise a frame with no route looks exactly like an idle client.
            if (ServerConfig.AppConfig.DebugMode && frame.Length >= 16)
            {
                SphLogger.Debug (
                    $"C->S unrouted {frame.Length}B frame, signature " +
                    $"{ClientPacketClassifier.ShiftedByte (frame, 13):X2} " +
                    $"{ClientPacketClassifier.ShiftedByte (frame, 14):X2} " +
                    $"{ClientPacketClassifier.ShiftedByte (frame, 15):X2} ({classification.Reason}): " +
                    // Identifying fields sit past the head, so the log keeps the whole frame
                    $"{Convert.ToHexString (frame)}. Client ID: {sphereClient.EntityID}");
            }

            return;
        }

        await DispatchClientPacketEvent (classification.Event, frame, delta);
    }

    private async Task DispatchClientPacketEvent (ClientPacketEvent packetEvent, byte[] frame, double delta)
    {
        switch (packetEvent)
        {
            case ClientPacketEvent.PositionKeepalive:
                seenFirstPositionKeepalive = true;
                await pingHandler!.Handle (frame, delta);
                sphereClient.UpdateCoordinatesInWorld ();
                break;
            case ClientPacketEvent.GroupAction:
                await groupActionsHandler!.Handle (frame, delta);
                break;
            case ClientPacketEvent.ItemPickup:
                await pickupItemHandler!.HandlePickupToNextAvailableEmptySlot (frame, delta);
                break;
            case ClientPacketEvent.ItemMove:
                await moveItemHandler!.Handle (frame, delta);
                break;
            case ClientPacketEvent.ItemUse:
                await useItemHandler!.Handle (frame, delta);
                break;
            case ClientPacketEvent.ChatSend:
                await clientChatHandler!.Accept (frame, delta);
                break;
            case ClientPacketEvent.ItemPickupToSlot:
                await pickupItemHandler!.HandlePickupToTargetSlot (frame, delta);
                break;
            case ClientPacketEvent.ContainerOpenLoot:
                await openLootContainerHandler!.Handle (frame, delta);
                break;
            case ClientPacketEvent.ItemDrop:
                await dropItemToGroundHandler!.Handle (frame, delta);
                break;
            case ClientPacketEvent.ItemDragOnGround:
                await dragItemOnGroundHandler!.Handle (frame, delta);
                break;
            case ClientPacketEvent.NpcInteract:
                await npcInteractionHandler!.Handle (frame, delta);
                break;
            case ClientPacketEvent.ItemTakeMainhand:
                await HandleTakeMainhand (frame, delta);
                break;
            case ClientPacketEvent.ItemSwap:
                await swapItemHandler!.Handle (frame, delta);
                break;
            case ClientPacketEvent.TradeBuy:
                await buyItemFromTargetHandler!.Handle (frame, delta);
                break;
            case ClientPacketEvent.CombatDamageTarget:
                await damageTargetHandler!.Handle (frame, delta);
                break;
            case ClientPacketEvent.NpcInteractClose:
                // The damage fallback used to send this ack; without it g_6008 stays set
                MaybeScheduleNetworkPacketSend (CommonPackets.ClearUseToutAck (sphereClient.EntityID));
                break;
            case ClientPacketEvent.ProtocolControl:
                // Keepalive is already echoed; other transport frames have no gameplay handler
                break;
            case ClientPacketEvent.CharacterSelect:
                // Before-game only; the in-game path never sees character select
                break;
            case ClientPacketEvent.StatsUpdateRequest:
                await changeStatsHandler!.Handle (frame, delta);
                break;
        }
    }

    private void InitHandlers ()
    {
        currentHandler ??= new HandshakeHandler (sphereClient.EntityID, this);
        pingHandler ??= new (sphereClient.EntityID, this);
        npcInteractionHandler ??= new (sphereClient.EntityID);
        clanActionsHandler ??= new ();
        groupActionsHandler ??= new (this);
        openLootContainerHandler ??= new (sphereClient.EntityID, this);
        clientChatHandler ??= new (this);
        pickupItemHandler ??= new (sphereClient.EntityID, this);
        dragItemOnGroundHandler ??= new (sphereClient.EntityID, this);
        moveItemHandler ??= new (sphereClient.EntityID, this);
        useItemHandler ??= new (sphereClient.EntityID, this);
        dropItemToGroundHandler ??= new ();
        mainhandTakeItemHandler ??= new (sphereClient.EntityID, this);
        swapItemHandler ??= new (sphereClient.EntityID, this);
        buyItemFromTargetHandler ??= new ();
        damageTargetHandler ??= new (sphereClient.EntityID, this);
        moveObjectForClientHandler ??= new (this);
        changeStatsHandler ??= new (sphereClient.EntityID, this);
    }

    public void MoveToNextBeforeGameStage ()
    {
        SphLogger.Info (
            $"Client moved from state: {sphereClient.ClientStateManager.CurrentState}. Client ID: {sphereClient.EntityID}");
        sphereClient.ClientStateManager.Transition ();
        currentHandler =
            BeforeGameHandlers.GetHandlerForState (sphereClient.ClientStateManager.CurrentState, sphereClient.EntityID,
                this);
        var handlerNameStr = currentHandler?.ToString () ?? "{none}";
        SphLogger.Info (
            $"New state: {sphereClient.ClientStateManager.CurrentState}. New handler: {handlerNameStr}. Client ID: {sphereClient.EntityID}");
    }

    public void Close ()
    {
        sphereClient.RemoveClient ();
    }

    public void BindBeforeGameUdp (Action<byte[]> send)
    {
        beforeGameSend = send;
    }

    public void FeedBeforeGame (byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return;
        }

        frameReader.Append (bytes);
        DrainFrames ();
    }

    /// <summary>
    /// One datagram can carry several frames, and the reader still splits on the length prefix
    /// </summary>
    private void DrainFrames ()
    {
        while (true)
        {
            var result = frameReader.TryTake (out var frame);

            if (result == FrameReadResult.Incomplete)
            {
                return;
            }

            if (result == FrameReadResult.Desynced)
            {
                SphLogger.Error (
                    $"Lost the frame boundary: {frameReader.DesyncReason}. " +
                    $"{frameReader.Pending} bytes held, {pendingFrames.Count} frames read first. " +
                    $"Closing. Client ID: {sphereClient.EntityID}");
                Close ();

                // Frames already read stay valid, but this connection is closing
                pendingFrames.Clear ();
                return;
            }

            SphPacketLogger.LogIncoming (sphereClient.EntityID, frame);
            if (TryHandleTransportFrame (frame))
            {
                continue;
            }

            pendingFrames.Enqueue (ShouldDecodeClientSubpacket (frame, sphereClient.EntityID)
                ? Packet.DecodeClientPacket (frame)
                : frame);
        }
    }

    public void SetPlayerDbEntry (PlayerDbEntry? entry)
    {
        sphereClient.SetPlayerDbEntry (entry);
    }

    public void ArmResume ()
    {
        secureResume = true;
    }

    public bool IsSecureResume => secureResume;

    public void SendResumeCharacterList ()
    {
        MaybeScheduleNetworkPacketSend (CommonPackets.ServerCredentials (sphereClient.EntityID));
        MaybeScheduleNetworkPacketSend (CommonPackets.CharacterSelectStartData (sphereClient.EntityID));
        SendCharacterList ();
    }

    public void SendCharacterList ()
    {
        var player = sphereClient.PlayerAccount;
        if (player is null)
        {
            return;
        }

        MaybeScheduleNetworkPacketSend (new PlayerDbEntrySerializer (player).ToInitialDataByteArray (sphereClient.EntityID));
    }

    public void EnterCharacterSelect ()
    {
        sphereClient.ClientStateManager.EnterCharacterSelect ();
        currentHandler = new CharacterSelectHandler (sphereClient.EntityID, this);
    }

    public void SaveSelectedCharacter ()
    {
        sphereClient.SaveCharacter ();
    }

    public bool DeletePlayerCharacter (int index)
    {
        return sphereClient.DeletePlayerCharacter (index);
    }

    public void CreatePlayerCharacter (CharacterDbEntry newCharacter, int index)
    {
        sphereClient.CreatePlayerCharacter (newCharacter, index);
    }

    public void SetSelectedCharacterIndex (int index)
    {
        sphereClient.SetSelectedCharacterIndex (index);
    }

    public CharacterDbEntry? GetSelectedCharacter ()
    {
        return sphereClient.GetSelecterCharacter ();
    }

    public void ReviveAfterDeath ()
    {
        sphereClient.ReviveAfterDeath ();
    }

    public void SendPacket (byte[] packet)
    {
        SphPacketLogger.LogOutgoing (sphereClient.EntityID, packet);
        beforeGameSend?.Invoke (packet);
    }

    public void MaybeScheduleNetworkPacketSend (byte[] packet)
    {
        // TODO: might need an actual queue. For now, just send
        SendPacket (packet);
    }

    /// <summary>
    /// Keepalive (0x01F4) is answered at once so Echo RTT stays elapsed/3; a sent-count frame would
    /// take a before-game tick
    /// </summary>
    private bool TryHandleTransportFrame (byte[] frame)
    {
        if (frame.Length < ClientFrame.HeaderLength)
        {
            return false;
        }

        var channel = (WireChannel) (frame[6] | (frame[7] << 8));
        if (channel == WireChannel.Keepalive)
        {
            SendPacket (CommonPackets.TransmissionEndPacket);
            return true;
        }

        return channel == WireChannel.SentCount;
    }

    private void MaybeSendStarterMutator ()
    {
        if (starterMutatorSent || timeSinceFirstPositionKeepalive < 3.0)
        {
            return;
        }

        MutatorHandler.SendSpecialMutator (this, sphereClient.EntityID, SpecialMutator.Прыг_х4, BelongingSlot.Mutator_1);
        MutatorHandler.SendSpecialMutator (this, sphereClient.EntityID, SpecialMutator.СХ, BelongingSlot.Mutator_2);
        starterMutatorSent = true;
    }

    public void EnqueueClientEvent (ClientQueuedEvent clientEvent)
    {
        sphereClient.EnqueueClientEvent (clientEvent);
    }

    public Task HandleTakeMainhand (byte[] frame, double delta)
    {
        mainhandTakeItemHandler ??= new (sphereClient.EntityID, this);
        return mainhandTakeItemHandler.Handle (frame, delta);
    }

    private static bool ShouldDecodeClientSubpacket (byte[] subpacket, Guid id)
    {
        if (subpacket.Length <= 12)
        {
            return false;
        }

        return !ClientSubpacketReferencesLocalPlayer (subpacket, id);
    }

    private static bool ClientSubpacketReferencesLocalPlayer (byte[] subpacket, Guid id)
    {
        var bytes = id.ToByteArray ();
        if (subpacket.Length < bytes.Length)
        {
            return false;
        }

        for (var i = 0; i <= subpacket.Length - bytes.Length; i++)
        {
            var match = true;
            for (var b = 0; b < bytes.Length; b++)
            {
                if (subpacket[i + b] != bytes[b])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }
}
