using Dapper;
using Microsoft.Extensions.Logging;
using Sphere.Common.Entities;
using Sphere.Common.Interfaces.Repository;

namespace Sphere.Repository.Repositories
{
    public abstract class BaseRepository<T> : IRepository<T> where T : BaseEntity
    {
        protected readonly ISqlConnectionFactory _connectionFactory;

        protected readonly ILogger _logger;

        protected abstract string TableName { get; }

        protected BaseRepository(ISqlConnectionFactory connectionFactory, ILogger logger)
        {
            _connectionFactory = connectionFactory;
            _logger = logger;
        }

        public async Task<T> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<T> CreateAsync(T entity, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
