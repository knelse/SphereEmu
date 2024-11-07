using Sphere.Common.Interfaces.Providers;

namespace Sphere.Services.Providers
{
    public class GuidIdentifierProvider : IIdentifierProvider<Guid>
    {
        public Guid GetIdentifier() => Guid.NewGuid();
    }
}
