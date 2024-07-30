using Godot;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sphere.Common.Interfaces;
using Sphere.Godot.Configuration.Services;
using Sphere.Server.Configuration.Options;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sphere.Godot.Nodes
{
    public partial class MainNode : Node
	{
        SceneTree tree = new SceneTree();

		public static void Main(string[] args)
		{
            
        }

        public override void _Ready()
        {
            var running = true;

            Console.WriteLine("Sphere server startup...");
            Console.WriteLine("Start service registration...");

            var serviceCollection = new ServiceCollection();

            var serviceProvider = serviceCollection
                .RegisterCommon()
                .RegisterServices()
                .BuildServiceProvider();

            Console.WriteLine("End service registration...");

            var logger = serviceProvider.GetRequiredService<ILogger<MainNode>>();

            var server = serviceProvider.GetRequiredService<IServer>();
            this.AddChild((Node)server);

            server.StartAsync();
            
            Task.Run(() =>
            {
                while (running)
                {
                    var command = Console.ReadLine();

                    switch (command)
                    {
                        case "stop":
                            server.StopAsync();
                            break;
                        case "start":
                            server.StartAsync();
                            break;
                        case "exit":
                            server.StopAsync();
                            running = false;
                            break;
                    }
                }

                ((Node)server).QueueFree();
                this.QueueFree();
            });
        }
    }

    public partial class Server : Node, IServer
	{
        private readonly TcpServer _listener;
        private readonly ILogger<Server> _logger;
        private readonly IOptions<ServerConfiguration> _serverConfiguration;
        private PackedScene _clientScene;
        private Dictionary<int, IClient> _portClientMap = new Dictionary<int, IClient>();

        public Server(ILogger<Server> logger, IOptions<ServerConfiguration> options)
		{
			_listener = new TcpServer();
            _logger = logger;
            _serverConfiguration = options;

            _clientScene = (PackedScene)ResourceLoader.Load("res://Client.tscn");
        }

        public override async void _Ready()
        {
            _listener.Listen(_serverConfiguration.Value.Port);
            _logger.LogInformation("Started server on port: [{port}]", _serverConfiguration.Value.Port);
        }

        public override async void _Process(double delta)
        {
            while (_listener.IsListening())
            {
                var tcpClient = _listener.TakeConnection();

                if (tcpClient != null)
                {
                    _logger.LogInformation("Connection accepted on port: [{port}]", tcpClient.GetLocalPort());

                    await HandleConnection(tcpClient);
                }
            }
        }

        private async Task HandleConnection(StreamPeerTcp client)
        {
            _logger.LogInformation("Handle connection started");

            client.SetNoDelay(true);

            var clientNode = _clientScene.Instantiate<ClientNode>();

            _logger.LogInformation("Handle connection finished");

            await Task.CompletedTask;
        }

        public Task StartAsync()
        {
            if (!_listener.IsListening())
            {
                _logger.LogInformation("Starting server on port: [{port}]", _serverConfiguration.Value.Port);

                _listener.Listen(_serverConfiguration.Value.Port);

                _logger.LogInformation("Started server on port: [{port}]", _serverConfiguration.Value.Port);

            }

            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            _logger.LogInformation("Stopping server on port: [{port}]", _serverConfiguration.Value.Port);

            _listener.Stop();
            
            _logger.LogInformation("Stopped server on port: [{port}]", _serverConfiguration.Value.Port);

            return Task.CompletedTask;
        }
    }
}
