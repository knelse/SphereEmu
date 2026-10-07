namespace SphServer.Server.Config;

/// <summary>
/// A throw here is logged by PreloadAll and stays off the packet path
/// </summary>
public interface IValidatableBalanceConfig
{
    void Validate (string configPath);
}
