using System.Data;

namespace Sphere.Common.Interfaces.Repository
{
    public interface ISqlConnectionFactory
    {
        IDbConnection CreateConnection();
    }
}
