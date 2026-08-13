using Godot;
using SphServer.Client;
using SphServer.Shared.Db;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;

namespace SphServer.Server.Debug;

/// <summary>
///     TEMP admin-UI helper: injects a fake connected client so the persona/stats panels
///     can be exercised without a live game client.
///     To remove later: delete this file and the <c>AdminDebugDummyClient.TrySpawn</c> call in
///     <see cref="SphereServer"/>.
/// </summary>
public static class AdminDebugDummyClient
{
    /// <summary>Flip to false (or delete this type) to disable without hunting call sites.</summary>
    public const bool Enabled = true;

    private const string Login = "knelse1";
    private const string CharacterName = "Test";

    public static void TrySpawn(Node parent, PackedScene clientScene)
    {
        if (!Enabled)
        {
            return;
        }

        var player = DbConnection.Players.Query()
            .Include(["$.Characters[*]", "$.Characters[*].Clan"])
            .Where(x => x.Login == Login)
            .FirstOrDefault();
        if (player is null)
        {
            SphLogger.Warning($"AdminDebugDummyClient: player login \"{Login}\" not found — skip");
            return;
        }

        var characterIndex = player.Characters.FindIndex(c => c.Name == CharacterName);
        if (characterIndex < 0)
        {
            SphLogger.Warning(
                $"AdminDebugDummyClient: character \"{CharacterName}\" not on \"{Login}\" — skip");
            return;
        }

        // BsonRef can hand back a thin stub; reload the Characters row so Items/stats match LiteDB.
        var characterId = player.Characters[characterIndex].Id;
        var character = DbConnection.Characters.Query()
            .Include(["$.Clan"])
            .Where(c => c.Id == characterId)
            .FirstOrDefault();
        if (character is null)
        {
            SphLogger.Warning(
                $"AdminDebugDummyClient: Characters id {characterId} missing — skip");
            return;
        }

        player.Characters[characterIndex] = character;

        var client = clientScene.Instantiate<SphereClient>();
        var id = ActiveClients.InsertAtFirstEmptyIndex(client);
        client.SetupAdminDebugDummy(id);
        client.SetPlayerDbEntry(player);
        client.SetSelectedCharacterIndex(characterIndex);
        client.CurrentCharacter?.RecalcAvailableStats();
        client.CurrentCharacter?.RecalcCurrentStats();
        client.SaveCharacter();

        ActiveNodes.Add(client.GetInstanceId(), client);
        parent.AddChild(client);

        SphLogger.Info(
            $"AdminDebugDummyClient: spawned {id:X4} as {Login}/{CharacterName} " +
            $"with {character.Items.Count} item slot(s) (debug only)");
    }
}
