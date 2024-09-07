using Godot;
using Microsoft.Extensions.Logging;
using Sphere.Common.Enums;
using Sphere.Common.Helpers.Extensions;
using Sphere.Common.Interfaces.Nodes;
using Sphere.Common.Interfaces.Packets;
using Sphere.Common.Interfaces.Providers;
using Sphere.Common.Interfaces.Readers;
using Sphere.Common.Interfaces.Repository;
using Sphere.Common.Interfaces.Services;
using Sphere.Common.Interfaces.Tcp;
using System;
using System.Threading.Tasks;
using static Sphere.Common.Packets.CommonPackets;

namespace Sphere.Godot.Nodes
{
    public class Client : IClient
	{
		private readonly ILogger<Client> _logger;
        private readonly ILocalIdProvider _localIdProvider;
        private static readonly PackedScene _clientScene;

		private ClientState _clientState = ClientState.I_AM_BREAD;
		private StaticBody3D? _clientModel;
        private IPacketReader _packetReader;
        private readonly IPacketHandler _basePacketHandler;
        private readonly IPacketParser _parser;
        private readonly IPlayersRepository _playerRepository;
        private readonly ITcpClientAccessor _tcpClientAccessor;

        public Node Node { get; private set; }

		public bool IsInGame => _clientState == ClientState.INGAME_DEFAULT;

        static Client()
        {
            _clientScene = (PackedScene)ResourceLoader.Load("res://Client.tscn");
        }

        public Client(ILogger<Client> logger, ILocalIdProvider localIdProvider, IPlayersRepository playersRepository, ITcpClientAccessor tcpClientAccessor, IPacketReader packetReader, IPacketHandler basePacketHandler, IPacketParser parser)
        {
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _localIdProvider = localIdProvider ?? throw new ArgumentNullException(nameof(localIdProvider));
            _playerRepository = playersRepository ?? throw new ArgumentNullException(nameof(playersRepository));
            _tcpClientAccessor = tcpClientAccessor ?? throw new ArgumentNullException(nameof(tcpClientAccessor));
            _packetReader = packetReader ?? throw new ArgumentNullException(nameof(packetReader));
            _basePacketHandler = basePacketHandler ?? throw new ArgumentNullException(nameof(basePacketHandler));
            _parser = parser ?? throw new ArgumentNullException(nameof(parser));
            var clientNode = _clientScene.Instantiate<ClientNode>();
            clientNode.SetClient(this);

            Node = clientNode;
        }

		public async Task InitializePlayer()
        {
            //switch (_clientState)
            //{
            //    case ClientState.I_AM_BREAD:
            //        _logger.LogInformation("CLI {playerId}: Ready to load initial data", _tcpClientAccessor.ClientId);

            //        SendPacket(ReadyToLoadInitialData);

            //        _clientState = ClientState.INIT_READY_FOR_INITIAL_DATA;

            //        break;
            //    case ClientState.INIT_READY_FOR_INITIAL_DATA:
            //        if (!TryReceivePacket(out var initPacket))
            //            return;

            //        _logger.LogInformation("CLI {playerId}: Connection initialized", _tcpClientAccessor.ClientId);

            //        var credentialsPacket = ServerCredentials(_tcpClientAccessor.ClientId.Value);
            //        SendPacket(credentialsPacket);

            //        _logger.LogInformation("SRV {playerId}: Credentials sent",_tcpClientAccessor.ClientId);

            //        _clientState = ClientState.INIT_WAITING_FOR_LOGIN_DATA;

            //        break;
            //    case ClientState.INIT_WAITING_FOR_LOGIN_DATA:
            //        if (!TryReceivePacket(out var loginPacket))
            //            return;

            //        _logger.LogInformation("CLI {playerId}: Login data sent", _tcpClientAccessor.ClientId);

            //        if (!LoginHelper.TryGetLoginAndPassword(loginPacket.OriginalMessage, out var loginPassword))
            //        {
            //            _logger.LogWarning("Can not parse login-password");
            //            return;
            //        }

            //        //var playerEntity = await _playerRepository.FindByLogin(loginPassword.Value.login, default);

            //        //if (playerEntity == null)
            //        //{
                       
            //        //}

            //        var player = new Player
            //        {
            //            Index = _tcpClientAccessor.ClientId.Value,
            //            Login = loginPassword.Value.login,
            //            PasswordHash = LoginHelper.GetHashedString(loginPassword.Value.password),
            //        };

            //        // check password




            //        _logger.LogInformation("Fetched char list data");
                    
            //        //if (player == null)
            //        //{
            //        //    // TODO: actual incorrect pwd packet
            //        //    Console.WriteLine($"SRV {playerIndexStr}: Incorrect password!");
            //        //    _tcpClient.PutData(AccountAlreadyInUse(LocalId));
            //        //    CloseConnection();

            //        //    return;
            //        //}

            //        SendPacket(CharacterSelectStartData(_tcpClientAccessor.ClientId.Value));
            //        _logger.LogInformation("SRV: Character select screen data - initial");
            //        Thread.Sleep(100);

            //        var playerInitialData = player.ToInitialDataByteArray();

            //        SendPacket(playerInitialData);
            //        _logger.LogInformation("SRV: Character select screen data - player characters");
            //        Thread.Sleep(100);
            //        _clientState = ClientState.INIT_WAITING_FOR_CHARACTER_SELECT;

            //        break;
            //    //case ClientState.INIT_WAITING_FOR_CHARACTER_SELECT:
            //    //    if (selectedCharacterIndex == -1)
            //    //    {
            //    //        if (StreamPeer.GetBytes(rcvBuffer) == 0x15)
            //    //        {
            //    //            selectedCharacterIndex = rcvBuffer[17] / 4 - 1;

            //    //            return;
            //    //        }

            //    //        if (rcvBuffer[0] == 0x2A)
            //    //        {
            //    //            if (rcvBuffer[0] == 0x2A)
            //    //            {
            //    //                var charIndex = rcvBuffer[17] / 4 - 1;
            //    //                var charId = player!.Characters[charIndex].Id;
            //    //                Console.WriteLine(
            //    //                    $"Delete character [{charIndex}] - [{player!.Characters[charIndex].Name}]");
            //    //                player!.Characters.RemoveAt(charIndex);
            //    //                MainServer.PlayerCollection.Update(player);
            //    //                MainServer.CharacterCollection.Delete(charId);

            //    //                // TODO: reinit session after delete
            //    //                // await HandleClientAsync(client, (ushort) (ID + 1), true);

            //    //                CloseConnection();
            //    //            }
            //    //        }

            //    //        if (rcvBuffer[0] < 0x1b ||
            //    //            rcvBuffer[13] != 0x08 || rcvBuffer[14] != 0x40 || rcvBuffer[15] != 0x80 ||
            //    //            rcvBuffer[16] != 0x05)
            //    //        {
            //    //            return;
            //    //        }

            //    //        selectedCharacterIndex = CharacterScreenCreateDeleteSelect();
            //    //    }

            //    //    if (selectedCharacterIndex == -1)
            //    //    {
            //    //        return;
            //    //    }

            //    //    CurrentCharacter = player!.Characters[selectedCharacterIndex];
            //    //    CurrentCharacter.ClientIndex = LocalId;
            //    //    // CurrentCharacter.X = 337;
            //    //    // CurrentCharacter.Y = -158;
            //    //    // CurrentCharacter.Z = -1450;
            //    //    CurrentCharacter.X = 424;
            //    //    CurrentCharacter.Y = -153;
            //    //    CurrentCharacter.Z = -1278;
            //    //    CurrentCharacter.Angle = 4;
            //    //    CurrentCharacter.Money = 99999999;

            //    //    Console.WriteLine("CLI: Enter game");
            //    //    StreamPeer.PutData(CurrentCharacter.ToGameDataByteArray());
            //    //    currentState = ClientState.INIT_WAITING_FOR_CLIENT_INGAME_ACK;
            //    //    break;
            //    //case ClientState.INIT_WAITING_FOR_CLIENT_INGAME_ACK:
            //    //    if (StreamPeer.GetBytes(rcvBuffer) == 0x13)
            //    //    {
            //    //        return;
            //    //    }
            //    //    // Interlocked.Increment(ref playerCount);

            //    //    var worldData = CommonPackets.NewCharacterWorldData(CurrentCharacter.ClientIndex);
            //    //    StreamPeer.PutData(worldData[0]);
            //    //    Thread.Sleep(50);
            //    //    // StreamPeer.PutData(worldData[1]);
            //    //    // currentState = ClientState.INIT_NEW_DUNGEON_TELEPORT_DELAY;
            //    //    // await ToSignal(GetTree().CreateTimer(3), "timeout");
            //    //    // currentState = ClientState.INIT_NEW_DUNGEON_TELEPORT_READY_TO_INIT;
            //    //    currentState = ClientState.INGAME_DEFAULT;
            //    //    break;
            //    case ClientState.INIT_NEW_DUNGEON_TELEPORT_DELAY:
            //        return;
            //    case ClientState.INIT_NEW_DUNGEON_TELEPORT_READY_TO_INIT:
            //        //await MoveToNewPlayerDungeonAsync(CurrentCharacter);
            //        // StreamPeer.PutData(CurrentCharacter.GetTeleportByteArray(new WorldCoords(2584, 160, 1426, -1.5)));
            //        break;
            //}
        }

        public async Task Process(double delta)
        {
            if (!_tcpClientAccessor.Client.Connected)
            {
                _logger.LogInformation("Player [{playerId}] disconnected, freeing resources...", _tcpClientAccessor.ClientId);
                this.Node.QueueFree();

                _localIdProvider.ReturnId(_tcpClientAccessor.ClientId);
            }

            if (_tcpClientAccessor.ClientState == ClientState.I_AM_BREAD)
            {
                await SendPacket(ReadyToLoadInitialData);

                var credentialsPacket = ServerCredentials(_tcpClientAccessor.ClientId);

                await SendPacket(credentialsPacket);
                _tcpClientAccessor.ClientState = ClientState.INIT_WAITING_FOR_LOGIN_DATA;
            }

            if (_tcpClientAccessor.Client.Available == 0)
                return;

            var packet = await ReceivePacket();

            if (packet == null)
                return;

            var parsedPacket = _parser.Parse(packet);

            await parsedPacket.Handle(_basePacketHandler, default);

            //if (!IsInGame)
            //{
            //    InitializePlayer();
            //    return;
            //}
        }

        private async ValueTask<PacketBase> ReceivePacket()
        {
            if (await _packetReader.MoveNextAsync())
            {
                var packet = _packetReader.Current;

                _logger.PacketReceived(packet, _tcpClientAccessor.ClientId);

                return packet;
            }

            return null;
        }

        private async Task SendPacket(byte[] rcvBuffer)
        {
            await _tcpClientAccessor.Client.WriteAsync(rcvBuffer);

            _logger.PacketSent(rcvBuffer, _tcpClientAccessor.ClientId);
        }
    }

    partial class ClientNode : Node
    {
        private Client _client;

        internal void SetClient(Client client)
        {
            _client = client;
        }

        public override async void _Process(double delta)
        {
            if (_client == null)
                return;

            await _client.Process(delta);
        }
    }

}
