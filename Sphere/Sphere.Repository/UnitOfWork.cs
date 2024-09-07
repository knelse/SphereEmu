using Sphere.Common.Interfaces.Repository;

namespace Sphere.Repository
{
    public class UnitOfWork : IUnitOfWork
    {
        public UnitOfWork(IPlayersRepository playersRepository, ICharacterRepository characterRepository)
        {
            PlayersRepository = playersRepository;
            CharacterRepository = characterRepository;
        }

        public IPlayersRepository PlayersRepository { get; }

        public ICharacterRepository CharacterRepository { get; }
    }
}
