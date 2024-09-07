using Sphere.Common.Interfaces.Tcp;
using System.Net.Sockets;

namespace Sphere.Services.Services.Tcp
{
    public class SphereTcpClient : ITcpClient, IDisposable
    {
        private readonly TcpClient _tcpClient;

        public SphereTcpClient(TcpClient tcpClient)
        {
            _tcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
        }

        public bool Connected => _tcpClient.Connected;

        public int Available => _tcpClient.Available;

        public void Close() => _tcpClient.Close();

        public async Task<int> ReadAsync(byte[] buffer, int offset, int count) => await GetStream().ReadAsync(buffer, offset, count);

        public async ValueTask WriteAsync(byte[] buffer) => await GetStream().WriteAsync(buffer);

        public Stream GetStream() => _tcpClient.GetStream();

        public void Dispose()
        {
            _tcpClient.Close();
        }
    }
}
