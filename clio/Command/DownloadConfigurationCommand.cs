using CommandLine;

namespace Clio.Command;

[Verb("download-configuration", Aliases = new[] { "dconf" },
    HelpText = "Download libraries from web-application")]
public class DownloadConfigurationCommandOptions : EnvironmentOptions
{
}

public class DownloadConfigurationCommand : Command<DownloadConfigurationCommandOptions>
{
    private readonly IApplicationDownloader _applicationDownloader;
    private readonly ILogger _logger;
    private readonly IWorkspace _workspace;

    public DownloadConfigurationCommand(IApplicationDownloader applicationDownloader, IWorkspace workspace,
        ILogger logger)
    {
        applicationDownloader.CheckArgumentNull(nameof(applicationDownloader));
        workspace.CheckArgumentNull(nameof(workspace));
        _applicationDownloader = applicationDownloader;
        _workspace = workspace;
        _logger = logger;
    }

    public override int Execute(DownloadConfigurationCommandOptions options)
    {
        _applicationDownloader.Download(_workspace.WorkspaceSettings.Packages);
        _logger.WriteLine("Done");
        return 0;
    }
}
