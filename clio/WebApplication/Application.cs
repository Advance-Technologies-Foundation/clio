using System.IO;
using System.Threading;

using Clio.Common;

namespace Clio.WebApplication;

public class Application : IApplication
{
    private readonly EnvironmentSettings _environmentSettings;
    private readonly IApplicationClient _applicationClient;
    private readonly IServiceUrlBuilder _serviceUrlBuilder;
    private readonly ILogger _logger;
    private readonly string uploadLicenseServiceUrl = "/ServiceModel/LicenseService.svc/UploadLicenses";

    public Application(EnvironmentSettings environmentSettings, IApplicationClient applicationClient,
        IServiceUrlBuilder serviceUrlBuilder, ILogger logger)
    {
        environmentSettings.CheckArgumentNull(nameof(environmentSettings));
        applicationClient.CheckArgumentNull(nameof(applicationClient));
        serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
        _environmentSettings = environmentSettings;
        _applicationClient = applicationClient;
        _serviceUrlBuilder = serviceUrlBuilder;
        _logger = logger;
    }

    private string GetCompleteUrl(string url) => _serviceUrlBuilder.Build(url);

    public void Restart()
    {
        _logger.WriteLine("Restart application...");
        string servicePath = _environmentSettings.IsNetCore
            ? @"/ServiceModel/AppInstallerService.svc/RestartApp"
            : @"/ServiceModel/AppInstallerService.svc/UnloadAppDomain";
        _applicationClient.ExecutePostRequest(GetCompleteUrl(servicePath), "{}", Timeout.Infinite);
    }

    public void LoadLicense(string licenseFilePath)
    {
        string fileData = File.ReadAllText(licenseFilePath);
        _ = $"{{ \"licData\":\"{fileData}\"}}";
        _applicationClient.ExecutePostRequest(uploadLicenseServiceUrl, licenseFilePath);
    }
}
