using Microsoft.Extensions.Logging;
using Sphere.Common.Interfaces.Providers;

namespace Sphere.Services.Providers
{
    public class LocalIdProvider : ILocalIdProvider
    {
        private static HashSet<ushort?> _localIds = new HashSet<ushort?>(ushort.MaxValue - 2);
        private readonly ILogger<LocalIdProvider> _logger;
        private static readonly object _lock = new object();

        public LocalIdProvider(ILogger<LocalIdProvider> logger, ushort maxNumber = ushort.MaxValue)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _localIds = Range(2, maxNumber).ToHashSet();
        }
        /// <summary>
        /// Gets first available identifier and removes it from the "pool"
        /// </summary>
        /// <returns></returns>
        /// <exception cref="OverflowException"></exception>
        public ushort GetIdentifier()
        {
            lock (_lock)
            {
                var id = _localIds.FirstOrDefault();

                if (id == null)
                {
                    _logger.LogError("Can not acquire free Local ID");
                    throw new OverflowException("Can not acquire free Local ID");
                }

                _localIds.Remove(id);

                _logger.LogInformation("Local ID [{localId}] has been acquired", id);

                return id.Value;
            }
        }

        /// <summary>
        /// returns freed identifier back to "pool"
        /// </summary>
        /// <param name="id"></param>
        public void ReturnId(ushort id)
        {
            if (_localIds.Add(id))
            {
                _logger.LogInformation("Local ID [{localId}] returned to the pool", id);
            }
            else
            {
                _logger.LogWarning("Local ID [{localId}] already existing in the pool!", id);
            }
        }

        /// <summary>
        /// ushort implementation of Enumerable.Range
        /// </summary>
        /// <param name="start"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        private static IEnumerable<ushort?> Range(ushort start, ushort count)
        {
            return RangeIterator(start, count);
        }

        static IEnumerable<ushort?> RangeIterator(ushort start, ushort count)
        {
            for (ushort i = 0; i <= count - 1; i++)
                yield return (ushort)(start + i);
        }
    }
}
