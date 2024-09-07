using Microsoft.Extensions.DependencyInjection;
using Sphere.Common.Interfaces.Repository;
using Sphere.Repository.Repositories;

namespace Sphere.Repository.Configuration
{
    public static class DependencyInjection
    {
        public static IServiceCollection RegisterRepositories(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<IPlayersRepository, PlayerRepository>();
            serviceCollection.AddSingleton<ICharacterRepository, CharacterRepository>();

            serviceCollection.AddSingleton<IUnitOfWork, UnitOfWork>();

            return serviceCollection;
        }
    }
}
