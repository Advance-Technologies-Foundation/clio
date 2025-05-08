using Clio.Common;
using System;

namespace Clio.Package;

public interface IApplicationLogProvider
{
    string GetInstallationLog(EnvironmentSettings environmentSettings);
}

public class ApplicationLogProvider : IApplicationLogProvider
{
    private const string InstallLogUrl = @"/ServiceModel/PackageInstallerService.svc/GetLogFile";

    protected string GetCompleteUrl(string url, EnvironmentSettings environmentSettings) =>
        _serviceUrlBuilder.Build(url, environmentSettings);

    public ApplicationLogProvider(IApplicationClientFactory applicationClientFactory,
        IServiceUrlBuilder serviceUrlBuilder)
    {
        _applicationClientFactory = applicationClientFactory;
        _serviceUrlBuilder = serviceUrlBuilder;
    }

    private IApplicationClientFactory _applicationClientFactory;
    private IServiceUrlBuilder _serviceUrlBuilder;

    private IApplicationClient CreateApplicationClient(EnvironmentSettings environmentSettings) =>
        _applicationClientFactory.CreateClient(environmentSettings);

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
}
