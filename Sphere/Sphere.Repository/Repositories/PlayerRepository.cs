using LiteDB;
using Microsoft.Extensions.Logging;
using Sphere.Common.Entities;
using Sphere.Common.Interfaces.Providers;
using Sphere.Common.Interfaces.Repository;

namespace Sphere.Repository.Repositories
{
    public class PlayerRepository : BaseRepositoryLite<PlayerEntity>, IPlayersRepository
    {
        protected override string TableName => "players";

        public PlayerRepository(ILiteDatabase db, ILogger<PlayerRepository> logger, IIdentifierProvider<Guid> identifierProvider) : base(db, logger, identifierProvider)
        {
        }

        public async Task<PlayerEntity> FindByLogin(string login, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(login, nameof(login));

            var result = _collection.Query().Where(x => x.Login == login).FirstOrDefault();

            return await Task.FromResult(result);
        }
    }
}
