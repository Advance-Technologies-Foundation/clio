using System;
using System.Collections.Generic;
using Clio.Common;
using Clio.Workspaces;
using CommandLine;

namespace Clio.Command;

#region Class: CreateWorkspaceCommandOptions

[Verb("create-workspace", Aliases = ["createw"], HelpText = "Create open project cmd file")]
public class CreateWorkspaceCommandOptions : WorkspaceOptions{
	#region Properties: Protected

	internal override bool RequiredEnvironment => false;

	#endregion
}

#endregion

#region Class: CreateWorkspaceCommand

public class CreateWorkspaceCommand : Command<CreateWorkspaceCommandOptions>{
	#region Fields: Private

	private readonly ILogger _logger;
	private readonly IInstalledApplication _installedApplication;

	private readonly IWorkspace _workspace;

	#endregion

	#region Constructors: Public

	public CreateWorkspaceCommand(IWorkspace workspace, ILogger logger, IInstalledApplication installedApplication) {
		workspace.CheckArgumentNull(nameof(workspace));
		_workspace = workspace;
		_logger = logger;
		_installedApplication = installedApplication;
	}

	#endregion

	#region Methods: Public

	public override int Execute(CreateWorkspaceCommandOptions options) {
		try {
			return InternalExecute(options);
		}
		catch (Exception e) {
			_logger.WriteError(e.Message);
			return 1;
		}
	}

	private int InternalExecute(CreateWorkspaceCommandOptions options) {
		if (options.Environment == null && string.IsNullOrEmpty(options.Uri)) {
			_workspace.Create(options.Environment);
			_logger.WriteInfo("Done");
			return 0;
		}

		bool appCodeNotExists = options.AppCode == null;
		_workspace.Create(options.Environment, appCodeNotExists);
		if (!appCodeNotExists) {
			InstalledAppInfo app = _installedApplication.GetInstalledAppInfo(options.AppCode);
			if (app != null) {
				IEnumerable<string> packages = app.GetPackages();
				foreach (string package in packages) {
					_workspace.AddPackageIfNeeded(package);
				}
			}
		}
		_workspace.Restore(options);
		_logger.WriteInfo("Done");
		return 0;
	}

	#endregion
}

#endregion
