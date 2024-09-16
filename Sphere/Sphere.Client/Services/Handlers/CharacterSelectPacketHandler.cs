using Microsoft.Extensions.Logging;
using Sphere.Common.Enums;
using Sphere.Common.Interfaces.Packets;
using Sphere.Common.Interfaces.Repository;
using Sphere.Common.Interfaces.Services;
using Sphere.Common.Interfaces.Tcp;
using Sphere.Common.Packets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sphere.Services.Services.Handlers
{
    public class CharacterSelectPacketHandler : BaseHandler, IPacketHandler<CharacterSelectPacket>
    {
        private readonly IUnitOfWork _unitOfWork;

        public CharacterSelectPacketHandler(ILogger<CharacterSelectPacketHandler> logger, IUnitOfWork unitOfWork, IClientAccessor tcpClientAccessor) : base(logger, tcpClientAccessor)
        {
            this._unitOfWork = unitOfWork;
        }

        public async Task Handle(IPacket packet, CancellationToken cancellationToken)
        {
            if (packet is not CharacterSelectPacket characterSelectPacket)
            {
                throw new ArgumentException($"Packet is not type of [{typeof(CharacterSelectPacket)}]");
            }

            if (characterSelectPacket.SelectedIndex < 0 || characterSelectPacket.SelectedIndex > 3)
            {
                throw new ArgumentException($"Selected index {characterSelectPacket.SelectedIndex} should be in range 0..2 ");
            }

            var playerCharacters = await _unitOfWork.CharacterRepository.GetPlayerCharacters(_clientAccessor.PlayerId, cancellationToken);

            var character = playerCharacters.FirstOrDefault(x=>x.Index == characterSelectPacket.SelectedIndex);

            if (character == null)
            {
                _logger.LogError("Character with index {index} was not found", characterSelectPacket.SelectedIndex);
                await TerminateConnection();
                return;
            }

            character.Coordinates = new Common.Models.Coordinates(424, -153, -1278, 4);
            character.Money = 99999999;

            await SendPacket(character.ToBytes(_clientAccessor.ClientId));

            var worldData = CommonPackets.NewCharacterWorldData(_clientAccessor.ClientId);
            await SendPacket(worldData);

            _clientAccessor.Character = character;
            _clientAccessor.ClientState = ClientState.INIT_WAITING_FOR_CLIENT_INGAME_ACK;
        }
    }
}
