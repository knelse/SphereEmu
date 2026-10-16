using System.Text;
using System.Threading.Tasks;
using BitStreams;
using Godot;
using SphereHelpers.Extensions;
using SphServer.Helpers.Networking;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.Networking.DataModel.Serializers;

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

            // select existing
            if (ClientPacketClassifier.IsCharacterSelect (frame))
            {
                selectedCharacterIndex = frame[17] / 4 - 1;
                return;
            }

            // Command 12 is the 1-based slot. Command 4 in that same frame deletes it.
            // A length of 0x2A is only the old delete capture; enter-game is 42 bytes too
            if (TryReadSlotCommand (frame, out var slot, out var delete))
            {
                var index = slot - 1;
                SphLogger.Info (
                    $"SRV {entityId}: slot command {slot} index {index} delete {delete} len {frame.Length}");
                if (delete)
                {
                    if (clientConnection.DeletePlayerCharacter (index))
                    {
                        clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.CharacterDeleted (entityId));
                    }

                    return;
                }

                selectedCharacterIndex = index;
                return;
            }

            SphLogger.Info (
                $"SRV {entityId}: character select unrecognized len {frame.Length} " +
                $"{Convert.ToHexString (frame.AsSpan (0, Math.Min (frame.Length, 32)))}");

            // create
            if (frame[0] < 0x1b ||
                frame[13] != 0x08 ||
                frame[14] != 0x40 ||
                frame[15] != 0x80 ||
                frame[16] != 0x05)
            {
                return;
            }

            selectedCharacterIndex = CreateNewCharacter (frame);
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

    private int CreateNewCharacter (byte[] rcvBuffer)
    {
        SphLogger.Info ($"SRV {entityId}: Creating new character");
        var len = rcvBuffer[0] - 20 - 5;
        var charDataBytesStart = rcvBuffer[0] - 5;
        var nameCheckBytes = rcvBuffer[20..];
        var charDataBytes = rcvBuffer[charDataBytesStart..rcvBuffer[0]];
        var sb = new StringBuilder ();
        var firstLetterCharCode = ((nameCheckBytes[1] & 0b11111) << 3) + (nameCheckBytes[0] >> 5);
        var firstLetterShouldBeRussian = false;

        for (var i = 1; i < len; i++)
        {
            var currentCharCode = ((nameCheckBytes[i] & 0b11111) << 3) + (nameCheckBytes[i - 1] >> 5);

            if (currentCharCode % 2 == 0)
            {
                // English
                var currentLetter = (char) (currentCharCode / 2);
                sb.Append (currentLetter);
            }
            else
            {
                // Russian
                var currentLetter = currentCharCode >= 193
                    ? (char) ((currentCharCode - 192) / 2 + 'а')
                    : (char) ((currentCharCode - 129) / 2 + 'А');
                sb.Append (currentLetter);

                if (i == 2)
                {
                    // we assume first letter was russian if second letter is, this is a hack
                    firstLetterShouldBeRussian = true;
                }
            }
        }

        string name;

        if (firstLetterShouldBeRussian)
        {
            firstLetterCharCode += 1;
            var firstLetter = firstLetterCharCode >= 193
                ? (char) ((firstLetterCharCode - 192) / 2 + 'а')
                : (char) ((firstLetterCharCode - 129) / 2 + 'А');
            name = firstLetter + sb.ToString ()[1..];
        }
        else
        {
            name = sb.ToString ();
        }

        var isNameValid = true; // Login.IsNameValid(name);

        if (!isNameValid)
        {
            SphLogger.Error ($"SRV {entityId}: Name [{name}] already exists!");
            clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.NameAlreadyExists (entityId));
            return -1;
        }

        SphLogger.Info ($"SRV {entityId}: Name [{name}] OK");

        var isGenderFemale = (charDataBytes[1] >> 4) % 2 == 1;
        var faceType = ((charDataBytes[1] & 0b111111) << 2) + (charDataBytes[0] >> 6);
        var hairStyle = ((charDataBytes[2] & 0b111111) << 2) + (charDataBytes[1] >> 6);
        var hairColor = ((charDataBytes[3] & 0b111111) << 2) + (charDataBytes[2] >> 6);
        var tattoo = ((charDataBytes[4] & 0b111111) << 2) + (charDataBytes[3] >> 6);

        if (isGenderFemale)
        {
            faceType = 256 - faceType;
            hairStyle = 255 - hairStyle;
            hairColor = 255 - hairColor;
            tattoo = 255 - tattoo;
        }

        var charIndex = rcvBuffer[17] / 4 - 1;

        var newCharacterData =
            CharacterDbEntry.CreateNewCharacter (entityId, name, isGenderFemale, faceType, hairStyle, hairColor, tattoo);

        clientConnection.CreatePlayerCharacter (newCharacterData, charIndex);

        clientConnection.MaybeScheduleNetworkPacketSend (CommonPackets.NameCheckPassed (entityId));

        SphLogger.Info ($"SRV {entityId}: Successfully created character [{name}]");

        return charIndex;
    }

    /// <summary>
    /// Connection command 12 is the 1-based slot. Command 4 in that frame deletes it.
    /// </summary>
    private static bool TryReadSlotCommand (byte[] frame, out int slot, out bool delete)
    {
        slot = 0;
        delete = false;
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
        var sawSlot = false;
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

            if (operation == 12 && count >= 1)
            {
                slot = stream.ReadUInt16 (8);
                for (var i = 1; i < count; i++)
                {
                    stream.ReadUInt16 (8);
                }

                sawSlot = true;
            }
            else
            {
                if (operation == 4 && count == 0 && sawSlot)
                {
                    delete = true;
                }

                for (var i = 0; i < count; i++)
                {
                    stream.ReadUInt16 (8);
                }
            }

            if (!Take (stream, frame, 7))
            {
                break;
            }

            wire = stream.ReadUInt16 (7);
        }

        return sawSlot;
    }

    private static bool Take (BitStream stream, byte[] frame, int bits)
    {
        return stream.BitOffsetFromStart + bits <= frame.Length * 8L;
    }
}
