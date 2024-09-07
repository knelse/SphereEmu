using Sphere.Common.Entities;

namespace Sphere.Common.Interfaces.Repository
{
    public interface ICharacterRepository : IRepository<CharacterEntity>
    {
        public Task<bool> IsNicknameOccupied(string nickname, CancellationToken cancellationToken);

        public Task<IReadOnlyCollection<CharacterEntity>> GetPlayerCharacters(Guid playerId, CancellationToken cancellationToken);
    }
}
