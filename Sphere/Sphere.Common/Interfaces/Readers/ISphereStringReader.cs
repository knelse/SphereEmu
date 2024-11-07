using Sphere.Common.Enums;

namespace Sphere.Common.Interfaces.Readers
{
    public interface ISphereStringReader
    {
        static abstract string Read(byte[] bytes, SphereStringType stringType);
    }
}
