using System;
using Clio.Common;

namespace Clio.Package;

public interface IApplicationLogProvider
{

    #region Methods: Public

    string GetInstallationLog(EnvironmentSettings environmentSettings);

    #endregion

}

public class ApplicationLogProvider : IApplicationLogProvider
{

    #region Constants: Private

    private const string InstallLogUrl = @"/ServiceModel/PackageInstallerService.svc/GetLogFile";

    #endregion

    #region Fields: Private

    private readonly IApplicationClientFactory _applicationClientFactory;
    private readonly IServiceUrlBuilder _serviceUrlBuilder;

    #endregion

    #region Constructors: Public

    public ApplicationLogProvider(IApplicationClientFactory applicationClientFactory,
        IServiceUrlBuilder serviceUrlBuilder)
    {
        _applicationClientFactory = applicationClientFactory;
        _serviceUrlBuilder = serviceUrlBuilder;
    }

    #endregion

    #region Methods: Private

    private IApplicationClient CreateApplicationClient(EnvironmentSettings environmentSettings) =>
        _applicationClientFactory.CreateClient(environmentSettings);

    #endregion

    #region Methods: Protected

    protected string GetCompleteUrl(string url, EnvironmentSettings environmentSettings) =>
        _serviceUrlBuilder.Build(url, environmentSettings);

    #endregion

    #region Methods: Public

    public string GetInstallationLog(EnvironmentSettings environmentSetting)
    {
        try
        {
            IApplicationClient applicationClientForLog = CreateApplicationClient(environmentSetting);
            return applicationClientForLog.ExecuteGetRequest(GetCompleteUrl(InstallLogUrl, environmentSetting));
        }
        catch (Exception)
        { }
        return string.Empty;
    }

    #endregion

}
