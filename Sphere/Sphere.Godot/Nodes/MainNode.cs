using Godot;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sphere.Common.Interfaces;
using Sphere.Common.Interfaces.Nodes;
using Sphere.Common.Interfaces.Providers;
using Sphere.Common.Interfaces.Tcp;
using Sphere.Godot.Configuration;
using Sphere.Godot.Configuration.Options;
using Sphere.Repository.Configuration;
using Sphere.Services.Services;
using Sphere.Services.Services.Tcp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Sphere.Godot.Nodes
{
    public partial class MainNode : Node
    {
        public static void Main(string[] args)
        {
            
        }

        public override void _Ready()
        {
            // add support for Win1251
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            var running = true;

            Console.WriteLine("Sphere server startup...");
            Console.WriteLine("Start service registration...");

            var serviceCollection = new ServiceCollection();

            var serviceProvider = serviceCollection
                .RegisterCommon(configuration)
                .RegisterServices()
                .RegisterRepositories()
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
            });
        }
    }

    public partial class Server : Node, IServer
    {
        private readonly TcpListener _listener;
        private readonly ILogger<Server> _logger;
        private readonly IOptions<ServerConfiguration> _serverConfiguration;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILocalIdProvider _localIdProvider;

        // private PackedScene _clientScene;
        private Dictionary<int, IClient> _portClientMap = new Dictionary<int, IClient>();

        public Server(ILogger<Server> logger, IOptions<ServerConfiguration> options, IServiceProvider serviceProvider, ILocalIdProvider localIdProvider)
        {
            _listener = new TcpListener(IPAddress.Any, options.Value.Port);
            _logger = logger;
            _serverConfiguration = options;
            _serviceProvider = serviceProvider;
            _localIdProvider = localIdProvider;
        }

        public override async void _Ready()
        {
        }

        public override void _Process(double delta)
        {
            if (!_listener.Pending())
                return;
            var tcpClient = _listener.AcceptTcpClient();

            if (tcpClient != null)
            {
                _logger.LogInformation("Connection accepted on port: [{port}]", ((IPEndPoint)tcpClient.Client.RemoteEndPoint).Port);
                tcpClient.NoDelay = true;
                
                HandleConnection(tcpClient);
            }
        }

        private async Task HandleConnection(TcpClient tcpClient)
        {
            _logger.LogInformation("Handle connection started");

            var scope = _serviceProvider.CreateScope();

            // setup current "context" accessor which grants access to tcpClient and clientId in all subsequent services in that scope
            var tcpClientAccessor = scope.ServiceProvider.GetRequiredService<ITcpClientAccessor>();
            tcpClientAccessor.Client = new SphereTcpClient(tcpClient);
            tcpClientAccessor.ClientId = _localIdProvider.GetIdentifier();
            tcpClientAccessor.ClientState = Common.Enums.ClientState.I_AM_BREAD;

            var client = scope.ServiceProvider.GetRequiredService<IClient>();

            this.AddChild(client.Node);

            _logger.LogInformation("Handle connection finished");

            await Task.CompletedTask;
        }

        public Task StartAsync()
        {
            if (!_listener.Server.IsBound)
            {
                _logger.LogInformation("Starting server on port: [{port}]", _serverConfiguration.Value.Port);

                _listener.Start();

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
