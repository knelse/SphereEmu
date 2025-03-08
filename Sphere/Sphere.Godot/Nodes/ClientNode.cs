using Godot;
using Microsoft.Extensions.Logging;
using Sphere.Common.Enums;
using Sphere.Common.Events.SpawnObject;
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
        private readonly IClientAccessor _tcpClientAccessor;

        public Node Node { get; private set; }

        private double timeSinceLastFifteenSecondPing = 1000;
        private double timeSinceLastSixSecondPing = 1000;
        private double timeSinceLastTransmissionEndPing = 1000;

        public bool IsInGame => _clientState == ClientState.INGAME_DEFAULT;

        static Client()
        {
            _clientScene = (PackedScene)ResourceLoader.Load("res://Client.tscn");
        }

        public Client(ILogger<Client> logger, ILocalIdProvider localIdProvider, IPlayersRepository playersRepository, IClientAccessor tcpClientAccessor, IPacketReader packetReader, IPacketHandler basePacketHandler, IPacketParser parser)
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

            if (parsedPacket == null) return;

            await parsedPacket.Handle(_basePacketHandler, default);
        }

        private async ValueTask<PacketBase> ReceivePacket()
        {
            if (await _packetReader.MoveNextAsync())
            {
                var packet = _packetReader.Current;

                return packet;
            }

            return null;
        }

        private async Task SendPacket(byte[] rcvBuffer)
        {
            await _tcpClientAccessor.Client.WriteAsync(rcvBuffer);

            _logger.PacketSent(rcvBuffer, _tcpClientAccessor.ClientId);
        }

        public void ClientConnected()
        {
            this._clientState = this._tcpClientAccessor.ClientState = ClientState.INGAME_DEFAULT;
            this._tcpClientAccessor.Server.SpawnEvent += Server_SpawnEvent;
            
        }

        private void Server_SpawnEvent(object sender, SpawnObjectEventArgs e)
        {
            try
            {
                var coordinates = e.Object.Coordinates;

                var playerCoords = this._tcpClientAccessor.Character.Coordinates;

                if (coordinates.Distance(playerCoords) >= 100)
                    return;

                var packet = e.Object.ToServerPacket();

                this.SendPacket(packet.GetBytes()).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
             //   _logger.LogError(ex, "Unhandled exception.");
            }
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
