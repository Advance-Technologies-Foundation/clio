using System;
using System.Collections.Generic;
using Clio.Common;
using Clio.Workspaces;
using CommandLine;

namespace Clio.Command;

[Verb("create-workspace", Aliases = new[] { "createw" }, HelpText = "Create open project cmd file")]
public class CreateWorkspaceCommandOptions : WorkspaceOptions
{
    internal override bool RequiredEnvironment => false;
}

public class CreateWorkspaceCommand : Command<CreateWorkspaceCommandOptions>
{
    private readonly ILogger _logger;
    private readonly IWorkspace _workspace;

    public CreateWorkspaceCommand(IWorkspace workspace, ILogger logger)
    {
        workspace.CheckArgumentNull(nameof(workspace));
        _workspace = workspace;
        _logger = logger;
    }

    public override int Execute(CreateWorkspaceCommandOptions options)
    {
        try
        {
            if (options.Environment == null && string.IsNullOrEmpty(options.Uri))
            {
                _workspace.Create(options.Environment);
            }
            else
            {
                bool appCodeNotExists = options.AppCode == null;
                _workspace.Create(options.Environment, appCodeNotExists);
                if (!appCodeNotExists)
                {
                    IInstalledApplication installedApplication = Program.Resolve<IInstalledApplication>(options);
                    InstalledAppInfo app = installedApplication.GetInstalledAppInfo(options.AppCode);
                    if (app != null)
                    {
                        IEnumerable<string> packages = app.GetPackages();
                        foreach (string package in packages)
                        {
                            _workspace.AddPackageIfNeeded(package);
                        }
                    }
                }

                _workspace.Restore(options);
            }

            _logger.WriteInfo("Done");
            return 0;
        }
        catch (Exception e)
        {
            _logger.WriteError(e.Message);
            return 1;
        }
    }
}
