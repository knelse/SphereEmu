namespace Sphere.Common.Interfaces
{
    public interface IServer
    {
        Task StartAsync();

        Task StopAsync();
    }
}
