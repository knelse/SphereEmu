using SphServer.Server.Config;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;

namespace SphServer.Client;

public partial class SphereClient
{
	public CharacterDbEntry? CurrentCharacter;
	private PlayerDbEntry? playerDbEntry;

	public void SetPlayerDbEntry (PlayerDbEntry? entry)
	{
		playerDbEntry = entry;
	}

	public PlayerDbEntry? PlayerAccount => playerDbEntry;

	public CharacterDbEntry? GetSelecterCharacter ()
	{
		return CurrentCharacter;
	}

	public string? GetLogin ()
	{
		return playerDbEntry?.Login;
	}

	public void SaveCharacter ()
	{
		// [BsonRef] character documents are a separate collection, so the player-row update leaves
		// them untouched
		if (CurrentCharacter is not null)
		{
			if (CurrentCharacter.Id == 0)
			{
				SphLogger.Error ($"SaveCharacter: character Id is 0, Insert instead. Client ID: {EntityID}");
			}

			CurrentCharacter.PersistAll ();
		}

		if (playerDbEntry is not null)
		{
			DbConnection.Players.Update (playerDbEntry);
		}

		DbConnection.Checkpoint ();
	}

	public void SetSelectedCharacterIndex (int index)
	{
		try
		{
			var listed = playerDbEntry!.Characters[index];
			// BsonRef Include can return a stub, so reload the Characters row for Base* and Items
			var character = listed.Id != 0
				? DbConnection.Characters.Query ()
					.Include (["$.Clan"])
					.Where (c => c.Id == listed.Id)
					.FirstOrDefault ()
				: null;
			if (character is null)
			{
				character = listed;
				SphLogger.Warning (
					$"SetSelectedCharacterIndex: no Characters row for id {listed.Id}, using list stub. " +
					$"Client ID: {EntityID}");
			}
			else
			{
				playerDbEntry.Characters[index] = character;
			}

			CurrentCharacter = character;
			CurrentCharacter.EntityId = EntityID;
			UpdateCharacterForDebugMode ();
			UpdateUnderwater ();
			ClientStateEvents.RaiseCharacterChanged (EntityID);
		}
		catch (Exception ex)
		{
			SphLogger.Error ($"Unable to select character at index: {index}. Client ID: {EntityID}.", ex);
		}
	}

	public void CreatePlayerCharacter (CharacterDbEntry newCharacter, int index)
	{
		try
		{
			DbConnection.Characters.Insert (newCharacter);
			playerDbEntry!.Characters.Insert (index, newCharacter);
			DbConnection.Players.Update (playerDbEntry!);
		}
		catch (Exception ex)
		{
			SphLogger.Error ($"Unable to create character at index: {index}. Client ID: {EntityID}.", ex);
		}
	}

	public bool DeletePlayerCharacter (int index)
	{
		// TODO: move to db entry
		try
		{
			var characterToDelete = playerDbEntry!.Characters[index];
			var id = characterToDelete.Id;
			var name = characterToDelete.Name;
			SphLogger.Info ($"Delete character [{index}] - [{name}]. Client ID: {EntityID}");
			playerDbEntry!.Characters.RemoveAt (index);
			DbConnection.Players.Update (playerDbEntry);
			DbConnection.Characters.Delete (id);
			return true;
		}
		catch (Exception ex)
		{
			SphLogger.Error ($"Unable to delete character at index: {index}. Client ID: {EntityID}.", ex);
			return false;
		}
	}

	private void UpdateCharacterForDebugMode ()
	{
		// TODO: move to db entry
		if (!ServerConfig.AppConfig.DebugMode)
		{
			SphLogger.Info ($"Skipping debug mode update (debug mode off). Client ID: {EntityID}");
			return;
		}

		if (CurrentCharacter is null)
		{
			SphLogger.Info ($"Skipping debug mode update (character is null). Client ID: {EntityID}");
			return;
		}

		CurrentCharacter.X = ServerConfig.AppConfig.Spawn_X;
		CurrentCharacter.Y = -ServerConfig.AppConfig.Spawn_Y;
		CurrentCharacter.Z = -ServerConfig.AppConfig.Spawn_Z;
		CurrentCharacter.Angle = ServerConfig.AppConfig.Spawn_Angle;
		CurrentCharacter.Money = ServerConfig.AppConfig.Spawn_Money;
	}
}
