namespace Sphere.Common.Interfaces.Providers
{
    public interface IIdentifierProvider<T>
    {
        T GetIdentifier();
    }
}
