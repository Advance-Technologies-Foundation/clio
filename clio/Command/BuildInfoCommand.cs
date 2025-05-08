using Clio.Command.CreatioInstallCommand;
using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command;

[Verb("get-build-info", Aliases = new[] { "buildinfo", "bi" }, HelpText = "Deploy Creatio from zip file")]
public class BuildInfoOptions : PfInstallerOptions
{
}

public class BuildInfoCommand(
    ISettingsRepository settingsRepository,
    ICreatioInstallerService creatioInstallerService,
    ILogger logger)
{
    private readonly ICreatioInstallerService _creatioInstallerService = creatioInstallerService;
    private readonly ILogger _logger = logger;

    public string RemoteArtefactServerPath { get; set; } = settingsRepository.GetRemoteArtefactServerPath();

    public string ProductFolder { get; set; } = settingsRepository.GetCreatioProductsFolder();

    public int Execute(BuildInfoOptions options)
    {
        string buildPath =
            _creatioInstallerService.GetBuildFilePathFromOptions(options.Product, options.DBType,
                options.RuntimePlatform);
        _logger.WriteInfo(buildPath);
        return 0;
    }
}
