using Sphere.Common.Entities;

namespace Sphere.Common.Interfaces.Repository
{
    public interface IRepository<T> where T : BaseEntity
    {
        Task<T> GetByIdAsync(Guid id, CancellationToken cancellationToken);

        Task<T> CreateAsync(T entity, CancellationToken cancellationToken);
    }
}
