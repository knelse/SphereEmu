using Godot;
using Microsoft.Extensions.Logging;
using Sphere.Common.Entities;
using Sphere.Common.Enums;
using Sphere.Common.Interfaces.Packets;
using Sphere.Common.Interfaces.Repository;
using Sphere.Common.Interfaces.Services;
using Sphere.Common.Interfaces.Tcp;
using Sphere.Common.Models;
using Sphere.Common.Packets;
using Sphere.Services.Services.Readers;

namespace Sphere.Services.Services.Handlers
{
    public class CharacterCreatePacketHandler : BaseHandler, IPacketHandler<CharacterCreatePacket>
    {
        private readonly IUnitOfWork _unitOfWork;

        public CharacterCreatePacketHandler(IUnitOfWork unitOfWork, ILogger<CharacterCreatePacketHandler> logger, ITcpClientAccessor tcpClientAccessor) : base(logger, tcpClientAccessor)
        {
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        }

        public async Task Handle(IPacket packet, CancellationToken cancellationToken)
        {
            if (packet is not CharacterCreatePacket createPacket)
            {
                throw new ArgumentException($"Packet is not type of [{typeof(CharacterCreatePacket)}]");
            }

            var name = SphereStringReader.Read(createPacket.Nickname, SphereStringType.Nickname);

            if (await _unitOfWork.CharacterRepository.IsNicknameOccupied(name, cancellationToken))
            {
                _logger.LogInformation("Character name is already occupied: [{nickname}]", name);
                await SendPacket(CommonPackets.NameAlreadyExists(_tcpClientAccessor.ClientId));

                return;
            }

            var existingPlayerChars = await _unitOfWork.CharacterRepository.GetPlayerCharacters(_tcpClientAccessor.PlayerId, cancellationToken);

            if (existingPlayerChars.Count >= 3)
            {
                _logger.LogError("Player {id} already has 3 or more characters", _tcpClientAccessor.PlayerId);
                await SendPacket(CommonPackets.TransmissionEndPacket);
                _tcpClientAccessor.Client.Close();
            }

            var charInfo = DecodeCharacterInfo(createPacket.CharacterInfo);
            var charIndex = createPacket.CharacterIndex / 4 - 1;

            var newChar = new CharacterEntity()
            {
                PlayerId = _tcpClientAccessor.PlayerId,
                Nickname = name,
                Gender = charInfo.Gender,
                FaceType = charInfo.Face,
                HairStyle = charInfo.Hair,
                HairColor = charInfo.HairColor,
                Tattoo = charInfo.Tattoo
            };

            await _unitOfWork.CharacterRepository.CreateAsync(newChar, cancellationToken);

            await SendPacket(CommonPackets.NameCheckPassed(_tcpClientAccessor.ClientId));
            await SendPacket(newChar.ToBytes(_tcpClientAccessor.ClientId));
        }

        private static CharacterInfo DecodeCharacterInfo(byte[] bytes)
        {
            var gender = (Gender)((bytes[1] >> 4) % 2);
            var faceType = ((bytes[1] & 0b111111) << 2) + (bytes[0] >> 6);
            var hairStyle = ((bytes[2] & 0b111111) << 2) + (bytes[1] >> 6);
            var hairColor = ((bytes[3] & 0b111111) << 2) + (bytes[2] >> 6);
            var tattoo = ((bytes[4] & 0b111111) << 2) + (bytes[3] >> 6);

            if (gender == Gender.Female)
            {
                faceType = 256 - faceType;
                hairStyle = 255 - hairStyle;
                hairColor = 255 - hairColor;
                tattoo = 255 - tattoo;
            }

            return new CharacterInfo(gender, faceType, hairStyle, hairColor, tattoo);
        }
    }
}
