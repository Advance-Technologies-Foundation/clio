using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.Workspaces;
using CommandLine;

namespace Clio.Command;

[Verb("cfg-worspace", Aliases = new[] { "cfgw" }, HelpText = "Configure workspace settings")]
public class ConfigureWorkspaceOptions : EnvironmentOptions
{
    [Option("Packages", Required = false, HelpText = "Packages")]
    public string Packages { get; set; }

    public IEnumerable<string> PackageNames
    {
        get
        {
            if (string.IsNullOrEmpty(Packages))
            {
                return Enumerable.Empty<string>();
            }

            return StringParser.ParseArray(Packages);
        }
    }
}

public class ConfigureWorkspaceCommand : Command<ConfigureWorkspaceOptions>
{
    private readonly ILogger _logger;
    private readonly IWorkspace _workspace;

    public ConfigureWorkspaceCommand(IWorkspace workspace, ILogger logger)
    {
        workspace.CheckArgumentNull(nameof(workspace));
        _workspace = workspace;
        _logger = logger;
    }

    public override int Execute(ConfigureWorkspaceOptions options)
    {
        try
        {
            foreach (string packageName in options.PackageNames)
            {
                _workspace.AddPackageIfNeeded(packageName);
            }

            _workspace.SaveWorkspaceEnvironment(options.Environment);
            return 0;
        }
        catch (Exception e)
        {
            _logger.WriteError(e.Message);
            return 1;
        }
    }
}
