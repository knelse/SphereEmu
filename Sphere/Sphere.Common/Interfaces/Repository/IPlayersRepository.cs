using Sphere.Common.Entities;

namespace Sphere.Common.Interfaces.Repository
{
    public interface IPlayersRepository : IRepository<PlayerEntity>
    {
        Task<PlayerEntity> FindByLogin(string login, CancellationToken cancellationToken);
    }
}
