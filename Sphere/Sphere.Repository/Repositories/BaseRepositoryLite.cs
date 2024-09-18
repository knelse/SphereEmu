using LiteDB;
using Microsoft.Extensions.Logging;
using Sphere.Common.Entities;
using Sphere.Common.Interfaces.Providers;
using Sphere.Common.Interfaces.Repository;

namespace Sphere.Repository.Repositories
{
    public abstract class BaseRepositoryLite<T> : IRepository<T> where T : BaseEntity
    {
        private readonly ILiteDatabase _database;
        private readonly ILogger _logger;
        private readonly IIdentifierProvider<Guid> _identifierProvider;
        protected readonly ILiteCollection<T> _collection;

        protected abstract string TableName { get; }

        protected BaseRepositoryLite(ILiteDatabase database, ILogger logger, IIdentifierProvider<Guid> identifierProvider)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _identifierProvider = identifierProvider ?? throw new ArgumentNullException(nameof(identifierProvider));

            _collection = database.GetCollection<T>(TableName);

        }

        public async Task<T> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            var result = _collection.Query().Where(x => x.Id == id).FirstOrDefault();

            return await Task.FromResult(result);
        }

        public async Task<T> CreateAsync(T entity, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(entity, nameof(entity));

            entity.Id = _identifierProvider.GetIdentifier();
            entity.CreatedDate = DateTime.UtcNow;
            entity.UpdatedDate = DateTime.UtcNow;

            _collection.Insert(entity);

            return await Task.FromResult(entity);
        }
    }
}
