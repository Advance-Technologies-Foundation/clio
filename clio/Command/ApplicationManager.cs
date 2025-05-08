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

public class ApplicationManager
{

    #region Fields: Private

    private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
    private readonly IDataProvider _dataProvider;
    private readonly IApplicationClientFactory _applicationClientFactory;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IApplicationInstaller _applicationInstallerserviceUrlBuilder;
    private readonly string _serviceApplicationExportPath = @"/ServiceModel/AppInstallerService.svc/ExportApp";

    #endregion

    #region Constructors: Public

    public ApplicationManager(IWorkingDirectoriesProvider workingDirectoriesProvider, IDataProvider dataProvider,
        ISettingsRepository settingsRepository, IApplicationClientFactory applicationClientFactory,
        IApplicationInstaller applicationInstallerserviceUrlBuilder)
    {
        _workingDirectoriesProvider = workingDirectoriesProvider;
        _dataProvider = dataProvider;
        _applicationClientFactory = applicationClientFactory;
        _settingsRepository = settingsRepository;
        _applicationInstallerserviceUrlBuilder = applicationInstallerserviceUrlBuilder;
    }

    #endregion

    #region Methods: Private

    private static string GetZipFilePath(string filePath, SysInstalledApp appInfo)
    {
        return string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(Environment.CurrentDirectory,
                $"{appInfo.Code}_{appInfo.Version}_{DateTime.UtcNow:dd-MMM-yyy_HH-mm}.zip")
            : filePath;
    }

    private void Install(string archivePath, string destinationEnvironmentCode)
    {
        EnvironmentSettings destinationEnvironment = _settingsRepository.GetEnvironment(destinationEnvironmentCode);
        _applicationInstallerserviceUrlBuilder.Install(archivePath, destinationEnvironment);
    }

    #endregion

    #region Methods: Internal

    internal void Deploy(string name, string sourceEnvironment, string destinationEnvironmentCode)
    {
        _workingDirectoriesProvider.CreateTempDirectory(tempDirectory =>
        {
            string archivePath = Path.Combine(tempDirectory, $"{name}.zip");
            Download(name, sourceEnvironment, archivePath);
            Install(archivePath, destinationEnvironmentCode);
        });
    }

    internal void Download(string name, string sourceEnvironmentCode, string filePath)
    {
        EnvironmentSettings sourceEnvironment = _settingsRepository.GetEnvironment(sourceEnvironmentCode);
        IApplicationClient sourceClient = _applicationClientFactory.CreateEnvironmentClient(sourceEnvironment);
        SysInstalledApp appInfo = GetAppFromAppName(name);
        var data = new
        {
            appId = appInfo.Id
        };
        string dataStr = JsonSerializer.Serialize(data);
        string zipFilePath = GetZipFilePath(filePath, appInfo);
        sourceClient.DownloadFile(_serviceApplicationExportPath, zipFilePath, dataStr);
    }

    #endregion

    #region Methods: Public

    public SysInstalledApp GetAppFromAppName(string name)
    {
        return GetApplicationList()
            .FirstOrDefault(a => a.Name.ToUpper() == name.ToUpper() || a.Code.ToUpper() == name.ToUpper());
    }

    public Guid GetAppIdFromAppName(string name) => GetAppFromAppName(name).Id;

    public List<SysInstalledApp> GetApplicationList() =>
        AppDataContextFactory.GetAppDataContext(_dataProvider)
                             .Models<SysInstalledApp>()
                             .ToList();

    #endregion

}
