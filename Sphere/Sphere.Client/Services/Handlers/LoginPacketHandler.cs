using Microsoft.Extensions.Logging;
using Sphere.Common.Entities;
using Sphere.Common.Enums;
using Sphere.Common.Helpers.Extensions;
using Sphere.Common.Interfaces.Packets;
using Sphere.Common.Interfaces.Repository;
using Sphere.Common.Interfaces.Services;
using Sphere.Common.Interfaces.Tcp;
using Sphere.Common.Packets.Client;
using Sphere.Services.Services.Readers;
using System.Text.RegularExpressions;
using static Sphere.Common.Packets.CommonPackets;

namespace Sphere.Services.Services.Handlers
{
    public class LoginPacketHandler : BaseHandler, IPacketHandler<LoginPacket>
    {
        private const int LoginStartPosition = 18;

        private readonly IUnitOfWork _unitOfWork;
        private static readonly Regex _validSymbols = new Regex("[A-z0-9_]{1,}");

        public LoginPacketHandler(ILogger<LoginPacketHandler> logger, IClientAccessor tcpClientAccessor, IUnitOfWork unitOfWork) : base(logger, tcpClientAccessor)
        {
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));

        }

        public async Task Handle(IPacket packet, CancellationToken cancellationToken)
        {
            if (packet is not LoginPacket loginPacket)
            {
                throw new ArgumentException($"Packet is not type of [{typeof(LoginPacket)}]");
            }

            if (_clientAccessor.ClientState != ClientState.INIT_WAITING_FOR_LOGIN_DATA)
            {
                _logger.LogError("Client is in wrong state [{currentState}], expected [{expectedState}]", _clientAccessor.ClientState, ClientState.INIT_WAITING_FOR_LOGIN_DATA);

                await TerminateConnection();
                return;
            }

            if (!TryGetLoginAndPassword(loginPacket.OriginalMessage, out var loginPassword))
            {
                _logger.LogError(
                    "Exception occurred while parsing login and password from client [{clientId}]",
                    _clientAccessor.ClientId);

                await TerminateConnection();
                return;
            }

            var playerEntity = await _unitOfWork.PlayersRepository.FindByLogin(loginPassword!.Value.login, default);

            if (playerEntity == null)
            {
                playerEntity = new PlayerEntity
                {
                    Login = loginPassword.Value.login,
                    Password = loginPassword.Value.password.GetHashedString(),
                };

                await _unitOfWork.PlayersRepository.CreateAsync(playerEntity, cancellationToken);
            }
            
            if (!loginPassword.Value.password.EqualsHashed(playerEntity.Password))
            {
                await SendPacket(AccountAlreadyInUse(_clientAccessor.ClientId));
                _logger.LogError("Incorrect password for player [{login}], clientId [{clientId}]", loginPassword.Value.login, _clientAccessor.ClientId);
                return;
            }

            _clientAccessor.PlayerId = playerEntity.Id;

            _logger.LogInformation("Fetched char list data");

            await SendPacket(CharacterSelectStartData(_clientAccessor.ClientId));
            _logger.LogInformation("SRV: Character select screen data - initial");

            var characters = await _unitOfWork.CharacterRepository.GetPlayerCharacters(playerEntity.Id, cancellationToken);
            var bytes = new List<byte>();
            for(var i = 0; i < 3; i++)
            {
                if (characters.Any(x => x.Index == i))
                {
                    bytes.AddRange(characters.ElementAt(i).ToCharacterListByteArray(_clientAccessor.ClientId));
                }
                else
                {
                    bytes.AddRange(CreateNewCharacterData(_clientAccessor.ClientId));
                }
            }

            var playerInitialData = playerEntity.ToInitialDataByteArray(_clientAccessor.ClientId);

            await SendPacket(bytes.ToArray());
            _logger.LogInformation("SRV: Character select screen data - player characters");

            _clientAccessor.ClientState = ClientState.INIT_WAITING_FOR_CHARACTER_SELECT;
        }

        private bool TryGetLoginAndPassword(byte[] rcvBuffer, out (string login, string password)? loginPassword)
        {
            loginPassword = null;

            try
            {
                var span = rcvBuffer.AsSpan()[LoginStartPosition..];

                var loginStopBytePosition = span.IndexOfAnyInRange((byte)0x00, (byte)0x03) + 1;
                var loginBytes = span[..loginStopBytePosition];

                var passwordStartPosition = loginBytes.Length;
                var passwordStopBytePosition = span[passwordStartPosition..].IndexOfAnyInRange((byte)0x00, (byte)0x03) + 1;
                var passwordBytes = span[passwordStartPosition..(passwordStartPosition + passwordStopBytePosition)];

                var parsedLogin = SphereStringReader.Read(loginBytes.ToArray(), SphereStringType.Login);
                var parsedPassword = SphereStringReader.Read(passwordBytes.ToArray(), SphereStringType.Login);

                if (!_validSymbols.IsMatch(parsedLogin) || !_validSymbols.IsMatch(parsedPassword))
                    return false;

                loginPassword = (parsedLogin, parsedPassword);

                return true;
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "Exception occurred while parsing login and password");
                return false;
            }
        }
    }
}
