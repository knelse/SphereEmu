using LiteDB;
using Microsoft.Extensions.Logging;
using Sphere.Common.Entities;
using Sphere.Common.Interfaces.Providers;
using Sphere.Common.Interfaces.Repository;

namespace Sphere.Repository.Repositories
{
    public class CharacterRepository : BaseRepositoryLite<CharacterEntity>, ICharacterRepository
    {
        protected override string TableName => "characters";

        public CharacterRepository(ILiteDatabase database, ILogger<CharacterRepository> logger, IIdentifierProvider<Guid> identifierProvider) : base(database, logger, identifierProvider)
        {
        }

        public async Task<bool> IsNicknameOccupied(string nickname, CancellationToken cancellationToken)
        {
            var result = _collection.Find(x => x.Nickname == nickname);

            return await Task.FromResult(result.Any());
        }

        public async Task<IReadOnlyCollection<CharacterEntity>> GetPlayerCharacters(Guid playerId, CancellationToken cancellationToken)
        {
            var result = _collection.Find(x => x.PlayerId == playerId);

            return await Task.FromResult(result.ToList());
        }
    }
}
