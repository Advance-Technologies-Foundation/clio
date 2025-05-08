using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Common;
using Clio.Package;
using Clio.UserEnvironment;
using CreatioModel;

namespace Clio.Command;

public class ApplicationManager(
    IWorkingDirectoriesProvider workingDirectoriesProvider,
    IDataProvider dataProvider,
    ISettingsRepository settingsRepository,
    IApplicationClientFactory applicationClientFactory,
    IApplicationInstaller applicationInstallerserviceUrlBuilder)
{
    private readonly IApplicationClientFactory _applicationClientFactory = applicationClientFactory;

    private readonly IApplicationInstaller _applicationInstallerserviceUrlBuilder =
        applicationInstallerserviceUrlBuilder;

    private readonly IDataProvider _dataProvider = dataProvider;
    private readonly string _serviceApplicationExportPath = @"/ServiceModel/AppInstallerService.svc/ExportApp";
    private readonly ISettingsRepository _settingsRepository = settingsRepository;
    private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider = workingDirectoriesProvider;

    public List<SysInstalledApp> GetApplicationList() =>
        AppDataContextFactory.GetAppDataContext(_dataProvider)
            .Models<SysInstalledApp>()
            .ToList();

    public SysInstalledApp GetAppFromAppName(string name) =>
        GetApplicationList()
            .FirstOrDefault(a => a.Name.ToUpper() == name.ToUpper() || a.Code.ToUpper() == name.ToUpper());

    public Guid GetAppIdFromAppName(string name) => GetAppFromAppName(name).Id;

    internal void Download(string name, string sourceEnvironmentCode, string filePath)
    {
        EnvironmentSettings sourceEnvironment = _settingsRepository.GetEnvironment(sourceEnvironmentCode);
        IApplicationClient sourceClient = _applicationClientFactory.CreateEnvironmentClient(sourceEnvironment);
        SysInstalledApp appInfo = GetAppFromAppName(name);
        var data = new { appId = appInfo.Id };
        string dataStr = JsonSerializer.Serialize(data);
        string zipFilePath = GetZipFilePath(filePath, appInfo);
        sourceClient.DownloadFile(_serviceApplicationExportPath, zipFilePath, dataStr);
    }

    private static string GetZipFilePath(string filePath, SysInstalledApp appInfo) =>
        string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(
                Environment.CurrentDirectory,
                $"{appInfo.Code}_{appInfo.Version}_{DateTime.UtcNow:dd-MMM-yyy_HH-mm}.zip")
            : filePath;

    internal void Deploy(string name, string sourceEnvironment, string destinationEnvironmentCode) =>
        _workingDirectoriesProvider.CreateTempDirectory(tempDirectory =>
        {
            string archivePath = Path.Combine(tempDirectory, $"{name}.zip");
            Download(name, sourceEnvironment, archivePath);
            Install(archivePath, destinationEnvironmentCode);
        });

    private void Install(string archivePath, string destinationEnvironmentCode)
    {
        EnvironmentSettings destinationEnvironment = _settingsRepository.GetEnvironment(destinationEnvironmentCode);
        _applicationInstallerserviceUrlBuilder.Install(archivePath, destinationEnvironment);
    }
}
