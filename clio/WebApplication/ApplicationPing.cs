using System;

using Common;

namespace Clio.WebApplication;

public interface IApplicationPing
{
    bool Ping();

    bool Ping(EnvironmentSettings environmentSettings);
}

public class ApplicationPing : IApplicationPing
{
    private readonly EnvironmentSettings _environmentSettings;
    private readonly IApplicationClientFactory _applicationClientFactory;
    private readonly IServiceUrlBuilder _serviceUrlBuilder;

    public ApplicationPing(
        EnvironmentSettings environmentSettings,
        IApplicationClientFactory applicationClientFactory, IServiceUrlBuilder serviceUrlBuilder)
    {
        environmentSettings.CheckArgumentNull(nameof(environmentSettings));
        applicationClientFactory.CheckArgumentNull(nameof(applicationClientFactory));
        serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
        _environmentSettings = environmentSettings;
        _applicationClientFactory = applicationClientFactory;
        _serviceUrlBuilder = serviceUrlBuilder;
    }

    private string PingUri => _environmentSettings.IsNetCore
        ? _environmentSettings.Uri
        : _serviceUrlBuilder.Build("ping");

    private IApplicationClient CreateApplicationClient(EnvironmentSettings environmentSettings) =>
        _applicationClientFactory.CreateClient(environmentSettings);

    public bool Ping(EnvironmentSettings environmentSettings)
    {
        try
        {
            IApplicationClient client = CreateApplicationClient(environmentSettings);
            client.ExecuteGetRequest(PingUri);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool Ping() => Ping(_environmentSettings);
}
