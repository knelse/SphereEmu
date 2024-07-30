using Godot;
using Microsoft.Extensions.Logging;
using Sphere.Common.Interfaces;
using System;

namespace Sphere.Godot.Nodes
{
	public partial class ClientNode : Node, IClient
	{
		private readonly ILogger<ClientNode> _logger;
		private StreamPeerTcp? _tcpClient;

		public ClientNode(ILogger<ClientNode> logger)
		{
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		public void SetupClient(StreamPeerTcp tcpClient)
		{
			_tcpClient = tcpClient;
		}

		public override void _Process(double delta)
		{
			base._Process(delta);
		}
	}
}
