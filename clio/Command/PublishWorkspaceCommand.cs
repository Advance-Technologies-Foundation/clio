﻿using System;
using Clio.Common;
using Clio.Workspaces;
using CommandLine;

namespace Clio.Command;

#region Class: PushWorkspaceCommandOptions

[Verb("publish-app", Aliases = new[]
{
    "publishw", "publish-hub", "ph", "publish-workspace"
}, HelpText = "Publish workspace to zip file")]
public class PublishWorkspaceCommandOptions : EnvironmentOptions
{

    #region Properties: Public

    [Option('h', "app-hub", Required = true,
        HelpText = "Path to application hub", Default = null)]
    public string AppHupPath { get; internal set; }

    [Option('a', "app-name", Required = true, HelpText = "Application name", Default = false)]
    public string AppName { get; internal set; }

    [Option('v', "app-version", Required = true,
        HelpText = "Application version", Default = null)]
    public string AppVersion { get; internal set; }

    [Option('b', "branch", Required = false,
        HelpText = "Branch name", Default = null)]
    public string Branch { get; set; }

    [Option('r', "repo-path", Required = true,
        HelpText = "Path to application workspace folder", Default = null)]
    public string WorkspaceFolderPath { get; internal set; }

    #endregion

}

#endregion

#region Class: PushWorkspaceCommand

public class PublishWorkspaceCommand : Command<PublishWorkspaceCommandOptions>
{

    #region Fields: Private

    private readonly IWorkspace _workspace;

    #endregion

    #region Constructors: Public

    public PublishWorkspaceCommand(IWorkspace workspace)
    {
        workspace.CheckArgumentNull(nameof(workspace));
        _workspace = workspace;
    }

    #endregion

    #region Methods: Public

    public override int Execute(PublishWorkspaceCommandOptions options)
    {
        try
        {
            _workspace.PublishToFolder(options.WorkspaceFolderPath, options.AppHupPath, options.AppName,
                options.AppVersion, options.Branch);
            Console.WriteLine("Done");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            return 1;
        }
    }

    #endregion

}

#endregion
