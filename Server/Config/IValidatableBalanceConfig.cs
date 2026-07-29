namespace SphServer.Server.Config;

/// <summary>Balance config that checks its own values at load time; throwing here is caught and logged by
/// <see cref="BalanceConfig.PreloadAll" />, which keeps the failure off the packet path.</summary>
public interface IValidatableBalanceConfig
{
    void Validate (string configPath);
}
