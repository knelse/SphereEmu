using System.Buffers.Binary;
using System.Threading.Tasks;
using BitStreams;
using SphereHelpers.Extensions;
using SphServer.Helpers.Networking;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.Networking.DataModel.Serializers;
using SphServer.System;

namespace SphServer.Client.Networking.Handlers.BeforeGame;

public class CharacterSelectHandler (Guid entityId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    private int selectedCharacterIndex = -1;

    public async Task Handle (byte[] frame, double delta)
    {
        if (selectedCharacterIndex == -1)
        {
            if (frame.Length == 0)
            {
                return;
            }

            if (!TryReadCharacterSelect (frame, out var slot, out var delete, out var createPayload,
                    out var listRequest))
            {
                return;
            }

            if (listRequest)
            {
                clientConnection.SendCharacterList ();
                return;
            }

            var index = slot - 1;
            if (delete)
            {
                if (clientConnection.DeletePlayerCharacter (index))
                {
                    clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.CharacterDeleted (entityId));
                }

                return;
            }

            if (createPayload is null)
            {
                selectedCharacterIndex = index;
                return;
            }

            selectedCharacterIndex = CreateNewCharacter (index, createPayload);
        }

        if (selectedCharacterIndex == -1)
        {
            // something went wrong
            return;
        }

        clientConnection.SetSelectedCharacterIndex (selectedCharacterIndex);

        var character = clientConnection.GetSelectedCharacter ();

        if (character is null)
        {
            // should never happen
            SphLogger.Error ($"SRV {entityId}: Selected character is null");
            return;
        }

        // TODO serializer field on object instead of creating them all the time
        // client wants game data here before it sends an ack, otherwise it will hang
        clientConnection.MaybeScheduleNetworkPacketSend (new CharacterDbEntrySerializer (character).ToGameDataByteArray ());
        clientConnection.MoveToNextBeforeGameStage ();
    }

    private int CreateNewCharacter (int charIndex, byte[] payload)
    {
        SphLogger.Info ($"SRV {entityId}: Creating new character");
        var nameLength = payload.Length - 5;
        var terminator = Array.IndexOf (payload, (byte) 0, 0, nameLength);
        if (terminator >= 0)
        {
            nameLength = terminator;
        }

        var name = SphEncoding.Win1251.GetString (payload, 0, nameLength);
        var isNameValid = true; // Login.IsNameValid(name);

        if (!isNameValid)
        {
            SphLogger.Error ($"SRV {entityId}: Name [{name}] already exists!");
            clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.NameAlreadyExists (entityId));
            return -1;
        }

        SphLogger.Info ($"SRV {entityId}: Name [{name}] OK");

        var signedModel = BinaryPrimitives.ReadInt32LittleEndian (payload.AsSpan (payload.Length - 4, 4));
        var isGenderFemale = signedModel < 0;
        var model = isGenderFemale ? 0u - (uint) signedModel : (uint) signedModel;
        var faceType = (int) (model & 0xFF);
        var hairStyle = (int) ((model >> 8) & 0xFF);
        var hairColor = (int) ((model >> 16) & 0xFF);
        var tattoo = (int) ((model >> 24) & 0xFF);

        var newCharacterData =
            CharacterDbEntry.CreateNewCharacter (entityId, name, isGenderFemale, faceType, hairStyle, hairColor, tattoo);

        clientConnection.CreatePlayerCharacter (newCharacterData, charIndex);

        clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.NameCheckPassed (entityId));

        SphLogger.Info ($"SRV {entityId}: Successfully created character [{name}]");

        return charIndex;
    }

    /// <summary>
    /// Region 0 commands: 12 + 4 enters the world, 12 + 7 registers a character, 13 deletes a slot
    /// </summary>
    private static bool TryReadCharacterSelect (byte[] frame, out int slot, out bool delete,
        out byte[]? createPayload, out bool listRequest)
    {
        slot = 0;
        delete = false;
        createPayload = null;
        listRequest = false;
        if (frame.Length < ClientFrame.HeaderLength ||
            (WireChannel) (frame[6] | (frame[7] << 8)) != WireChannel.Gameplay)
        {
            return false;
        }

        var stream = new BitStream (frame);
        var bodyBit = ClientFrame.BodyOffset * 8;
        if (bodyBit >= frame.Length * 8)
        {
            return false;
        }

        stream.SeekBitOffset (bodyBit);
        if (!Take (stream, frame, 1 + 15 + 128 + 12 + 7))
        {
            return false;
        }

        if (stream.ReadUInt16 (1) != 0)
        {
            stream.SeekForward (45);
        }

        stream.SeekForward (15 + 128 + 12);
        var wire = stream.ReadUInt16 (7);
        var enterWorld = false;
        while (wire is 1 or 0x3F)
        {
            if (wire == 0x3F)
            {
                if (!Take (stream, frame, 128 + 12 + 7))
                {
                    break;
                }

                stream.SeekForward (128 + 12);
                wire = stream.ReadUInt16 (7);
                continue;
            }

            if (!Take (stream, frame, 5 + 8))
            {
                break;
            }

            var operation = stream.ReadUInt16 (5);
            var count = stream.ReadUInt16 (8);
            if (count > 64 || !Take (stream, frame, count * 8))
            {
                break;
            }

            var payload = new byte[count];
            for (var i = 0; i < count; i++)
            {
                payload[i] = (byte) stream.ReadUInt16 (8);
            }

            if (operation == 12 && count >= 1)
            {
                slot = payload[0];
            }
            else if (operation == 4 && count == 0)
            {
                if (slot > 0)
                {
                    enterWorld = true;
                }
                else
                {
                    listRequest = true;
                }
            }
            else if (operation == 7 && count >= 5 && slot > 0)
            {
                createPayload = payload;
            }
            else if (operation == 13 && count >= 4)
            {
                slot = BinaryPrimitives.ReadInt32LittleEndian (payload);
                delete = true;
            }

            if (!Take (stream, frame, 7))
            {
                break;
            }

            wire = stream.ReadUInt16 (7);
        }

        return enterWorld || delete || createPayload is not null || listRequest;
    }

    private static bool Take (BitStream stream, byte[] frame, int bits)
    {
        return stream.BitOffsetFromStart + bits <= frame.Length * 8L;
    }
}
