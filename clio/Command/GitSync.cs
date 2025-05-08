using System.IO;
using System.Runtime.InteropServices;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("git-sync", Aliases = new[] { "sync" }, HelpText = "Syncs environment with Git repository")]
public class GitSyncOptions : EnvironmentNameOptions
{
    [Option("Direction", Required = true, HelpText = "Sets sync direction")]
    public string Direction { get; set; }
}

public class GitSyncCommand(EnvironmentSettings settings, IProcessExecutor processExecutor, ILogger logger)
    : Command<GitSyncOptions>
{
    private readonly ILogger _logger = logger;
    private readonly IProcessExecutor _processExecutor = processExecutor;
    private readonly EnvironmentSettings _settings = settings;

    public override int Execute(GitSyncOptions options)
    {
        string ext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd" : "sh";

        string fileName = options.Direction.ToLower().IndexOf("git") < options.Direction.ToLower().IndexOf("env")
            ? "git-to-env"
            : "env-to-git";

        string batPath = Path.Join(_settings.WorkspacePathes, "tasks", $"{fileName}.{ext}");

        string result = _processExecutor.Execute(
            batPath,
            options.Environment, true, _settings.WorkspacePathes, true);

        _logger.WriteInfo(result);

        return 0;
    }
}
