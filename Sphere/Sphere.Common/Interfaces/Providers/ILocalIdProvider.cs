namespace Sphere.Common.Interfaces.Providers
{
    public interface ILocalIdProvider : IIdentifierProvider<ushort>
    {
        void ReturnId(ushort id);
    }
}
