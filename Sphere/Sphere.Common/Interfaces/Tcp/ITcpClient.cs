namespace Sphere.Common.Interfaces.Tcp
{
    public interface ITcpClient
    {
        Stream GetStream();

        bool Connected { get; }

        int Available { get; }

        void Close();

        Task<int> ReadAsync(byte[] buffer, int offset, int count);

        ValueTask WriteAsync(byte[] buffer);
    }
}
