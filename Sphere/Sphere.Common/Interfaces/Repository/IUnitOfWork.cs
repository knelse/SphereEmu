namespace Sphere.Common.Interfaces.Repository
{
    public interface IUnitOfWork
    {
        IPlayersRepository PlayersRepository { get; }

        ICharacterRepository CharacterRepository { get; }
    }
}
