using System;
using Clio.Common;

namespace Clio.Package;

public interface IApplicationLogProvider
{
    string GetInstallationLog(EnvironmentSettings environmentSettings);
}

public class ApplicationLogProvider(
    IApplicationClientFactory applicationClientFactory,
    IServiceUrlBuilder serviceUrlBuilder) : IApplicationLogProvider
{
    private const string InstallLogUrl = @"/ServiceModel/PackageInstallerService.svc/GetLogFile";

    private readonly IApplicationClientFactory _applicationClientFactory = applicationClientFactory;
    private readonly IServiceUrlBuilder _serviceUrlBuilder = serviceUrlBuilder;

    public string GetInstallationLog(EnvironmentSettings environmentSetting)
    {
        try
        {
            IApplicationClient applicationClientForLog = CreateApplicationClient(environmentSetting);
            return applicationClientForLog.ExecuteGetRequest(GetCompleteUrl(InstallLogUrl, environmentSetting));
        }
        catch (Exception)
        {
        }

        return string.Empty;
    }

    protected string GetCompleteUrl(string url, EnvironmentSettings environmentSettings) =>
        _serviceUrlBuilder.Build(url, environmentSettings);

    private IApplicationClient CreateApplicationClient(EnvironmentSettings environmentSettings) =>
        _applicationClientFactory.CreateClient(environmentSettings);
}
