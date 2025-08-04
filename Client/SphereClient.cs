using System;
using Godot;
using SphServer.Client.Networking;
using SphServer.Client.State;
using SphServer.Server.Config;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;

namespace SphServer.Client;

public partial class SphereClient : Node
{
    private ClientState currentState = ClientState.I_AM_BREAD;

    private StaticBody3D? clientModel;

    public CharacterDbEntry? CurrentCharacter;
    private ClientConnection clientConnection;
    public readonly ClientStateManager ClientStateManager = new (false);
    private ushort localId;
    private StreamPeerTcp streamPeerTcp = null!;
    private PlayerDbEntry? playerDbEntry;
    private bool isExiting;
    private int selectedCharacterIndex;

    public override async void _Process (double delta)
    {
        if (streamPeerTcp.GetStatus() != StreamPeerTcp.Status.Connected)
        {
            RemoveClient();
            return;
        }

        clientModel ??= GetNode<StaticBody3D>("ClientModel");

        clientConnection.Process(delta);
    }

    public override void _Ready ()
    {
        // TODO: client logs in separate files
        SphLogger.Info($"New client connected. Client ID: {localId}");
        // Task.Run(() =>
        // {
        //     while (true)
        //     {
        //         var input = Console.ReadLine();
        //         try
        //         {
        //             if (CurrentCharacter is null)
        //             {
        //                 Console.WriteLine("Character is null");
        //                 continue;
        //             }
        //
        //             var parser = ConsoleCommandParser.Get(CurrentCharacter);
        //             var result = parser.Parse(input);
        //         }
        //         catch (Exception ex)
        //         {
        //             Console.WriteLine(ex.Message);
        //         }
        //     }
        // });
    }

    public SphereClient Setup (StreamPeerTcp streamPeer, ushort id)
    {
        clientConnection = new ClientConnection(streamPeer, id, this);
        localId = id;
        streamPeerTcp = streamPeer;

        return this;
    }

    public void SetPlayerDbEntry (PlayerDbEntry? entry)
    {
        playerDbEntry = entry;
    }

    public CharacterDbEntry? GetSelecterCharacter ()
    {
        return CurrentCharacter;
    }

    public void SetSelectedCharacterIndex (int index)
    {
        selectedCharacterIndex = index;
        try
        {
            CurrentCharacter = playerDbEntry!.Characters[index];
            CurrentCharacter.ClientIndex = localId;
            UpdateCharacterForDebugMode();
        }
        catch (Exception ex)
        {
            SphLogger.Error($"Unable to select character at index: {index}. Client ID: {localId}.", ex);
        }
    }

    public void CreatePlayerCharacter (CharacterDbEntry newCharacter, int index)
    {
        try
        {
            DbConnection.Characters.Insert(newCharacter);
            playerDbEntry!.Characters.Insert(index, newCharacter);
            DbConnection.Players.Update(playerDbEntry!);
        }
        catch (Exception ex)
        {
            SphLogger.Error($"Unable to create character at index: {index}. Client ID: {localId}.", ex);
        }
    }

    public void DeletePlayerCharacter (int index)
    {
        // TODO: move to db entry
        try
        {
            var characterToDelete = playerDbEntry!.Characters[index];
            var id = characterToDelete.Id;
            var name = characterToDelete.Name;
            SphLogger.Info($"Delete character [{index}] - [{name}]. Client ID: {localId}");
            playerDbEntry!.Characters.RemoveAt(index);
            DbConnection.Players.Update(playerDbEntry);
            DbConnection.Characters.Delete(id);

            // TODO: reinit session after delete
            RemoveClient();
        }
        catch (Exception ex)
        {
            SphLogger.Error($"Unable to delete character at index: {index}. Client ID: {localId}.", ex);
        }
    }

    public void RemoveClient ()
    {
        if (isExiting)
        {
            return;
        }

        SphLogger.Info($"Client disconnected. Client ID: {localId}");

        isExiting = true;
        // TODO: sync state
        clientConnection.Close();
        ActiveClients.Remove(localId);
        ActiveClients.Remove(localId);
        QueueFree();
    }

    private void UpdateCharacterForDebugMode ()
    {
        // TODO: move to db entry
        if (!ServerConfig.AppConfig.DebugMode)
        {
            SphLogger.Info($"Skipping debug mode update (debug mode off). Client ID: {localId}");
            return;
        }

        if (CurrentCharacter is null)
        {
            SphLogger.Info($"Skipping debug mode update (character is null). Client ID: {localId}");
            return;
        }

        CurrentCharacter.X = ServerConfig.AppConfig.Spawn_X;
        CurrentCharacter.Y = -ServerConfig.AppConfig.Spawn_Y;
        CurrentCharacter.Z = ServerConfig.AppConfig.Spawn_Z;
        CurrentCharacter.Angle = ServerConfig.AppConfig.Spawn_Angle;
        CurrentCharacter.Money = ServerConfig.AppConfig.Spawn_Money;
    }
}