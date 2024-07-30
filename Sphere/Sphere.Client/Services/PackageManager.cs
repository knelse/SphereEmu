using Microsoft.Extensions.Logging;
using Sphere.Common.Interfaces.Services;

namespace Sphere.Client.Services
{
    public class PackageManager : IPackageManager
    {
        private readonly ILogger<PackageManager> _logger;

        public PackageManager(ILogger<PackageManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
    }
}
